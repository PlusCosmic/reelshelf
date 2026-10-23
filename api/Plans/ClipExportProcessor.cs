using Microsoft.Extensions.Options;
using Reelshelf.Email;

namespace Reelshelf.Plans;

/// <summary>
/// Makes exports one at a time: claims the next (<see cref="IClipExportStore.Claim"/>), zips its clips into R2
/// (<see cref="ClipExportBuilder"/>), marks it ready for <see cref="PlanOptions.ExportRetention"/> and mails the
/// owner. A grace's clips are deleted by the sweep once its export is ready, never before. A failed attempt is
/// retried after <see cref="PlanOptions.ExportRetryAfter"/>, up to <see cref="PlanOptions.ExportMaxAttempts"/>.
/// </summary>
public sealed class ClipExportProcessor(
    IClipExportStore exports,
    ClipExportBuilder builder,
    IExportStorage storage,
    IPlanEnforcementStore accounts,
    IEmailSender emailSender,
    IOptionsMonitor<PlanOptions> options,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<ClipExportProcessor> logger)
{
    private readonly string _planUrl = PlanUrl(configuration);

    public static string PlanUrl(IConfiguration configuration) =>
        (configuration["FrontendOrigin"] ?? "http://localhost:5173").TrimEnd('/') + "/plan";

    /// <summary>Makes the next export, if there is one. True when one was claimed.</summary>
    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        PlanOptions current = options.CurrentValue;
        ClipExport? export = await exports.Claim(current.ExportStaleAfter);
        if (export is null)
        {
            return false;
        }

        try
        {
            List<ExportClipRow> clips = await exports.GetExportClips(export.UserId, export.ClipIds);
            List<ClipExportPart> parts = await builder.BuildAsync(export, clips, current.ExportMaxPartBytes,
                () => exports.Heartbeat(export.Id), cancellationToken);
            DateTimeOffset expiresAt = timeProvider.GetUtcNow() + current.ExportRetention;
            await exports.Complete(export.Id, parts, expiresAt);
            int clipCount = parts.Sum(part => part.ClipCount);
            logger.LogInformation("Export {ExportId} of account {UserId} is ready: {Clips} clips in {Parts} parts",
                export.Id, export.UserId, clipCount, parts.Count);

            OverLimitState state = await accounts.GetState(export.UserId);
            if (!string.IsNullOrEmpty(state.Email))
            {
                await emailSender.SendAsync(AccountEmails.ExportReady(state.Email, clipCount,
                    export.Reason == ClipExportReasons.GraceEnded, expiresAt, _planUrl), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down; the export goes stale and is claimed again.
            throw;
        }
        catch (Exception error)
        {
            bool retry = export.Attempts < current.ExportMaxAttempts;
            logger.LogError(error, "Export {ExportId} of account {UserId} failed (attempt {Attempt}){Retry}",
                export.Id, export.UserId, export.Attempts, retry ? "; it will be retried" : "");
            await storage.DeletePrefixAsync(ExportParts.Prefix(export.UserId, export.Id));
            await exports.Fail(export.Id, error.Message, retry ? current.ExportRetryAfter : null);
        }

        return true;
    }

    /// <summary>Deletes the zips of exports past their expiry. The bucket's lifecycle rule is the backstop.</summary>
    public async Task ExpireAsync()
    {
        foreach (ClipExport export in await exports.ExpireDue())
        {
            await storage.DeletePrefixAsync(ExportParts.Prefix(export.UserId, export.Id));
            logger.LogInformation("Export {ExportId} of account {UserId} expired; its zips were deleted",
                export.Id, export.UserId);
        }
    }
}

/// <summary>Runs <see cref="ClipExportProcessor"/> while R2 is configured. Never in the <c>OpenApi</c> environment.</summary>
public sealed class ClipExportBackgroundService(
    IServiceScopeFactory scopeFactory,
    IExportStorage storage,
    IOptionsMonitor<PlanOptions> options,
    ILogger<ClipExportBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!storage.IsConfigured)
        {
            logger.LogInformation("Exports are off: R2 isn't configured");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            bool busy = false;
            try
            {
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                ClipExportProcessor processor = scope.ServiceProvider.GetRequiredService<ClipExportProcessor>();
                await processor.ExpireAsync();
                busy = await processor.ProcessNextAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error)
            {
                logger.LogError(error, "The export service failed; trying again shortly");
            }

            if (!busy)
            {
                await Task.Delay(options.CurrentValue.ExportPollInterval, stoppingToken);
            }
        }
    }
}
