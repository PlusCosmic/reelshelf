using Microsoft.Extensions.Options;
using Reelshelf.Email;

namespace Reelshelf.Plans;

/// <summary>
/// Makes exports one at a time: claims the next (<see cref="IClipExportStore.Claim"/>), zips its clips into R2
/// (<see cref="ClipExportBuilder"/>), marks it ready for <see cref="PlanOptions.ExportRetention"/> and mails the
/// owner. A grace's clips are deleted by the sweep once its export is ready, never before. A failed attempt is
/// retried after <see cref="PlanOptions.ExportRetryAfter"/>, up to <see cref="PlanOptions.ExportMaxAttempts"/>
/// (a run that went quiet and was reclaimed counts as one too).
/// Each run holds the lease its claim gave it and writes under its own folder (<see cref="ExportParts.RunPrefix"/>);
/// once a heartbeat, completion or failure finds the lease gone (the export was reclaimed), the run stops, deletes
/// only its own folder and leaves the export to whoever holds it now.
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
        ClipExport? export = await exports.Claim(current.ExportStaleAfter, current.ExportMaxAttempts);
        if (export is null)
        {
            return false;
        }

        Guid lease = export.LeaseId ??
            throw new InvalidOperationException($"Export {export.Id} was claimed without a lease");
        List<ClipExportPart> parts;
        DateTimeOffset expiresAt;
        try
        {
            List<ExportClipRow> clips = await exports.GetExportClips(export.UserId, export.ClipIds);
            parts = await builder.BuildAsync(export, clips, current.ExportMaxPartBytes,
                async () =>
                {
                    if (!await exports.Heartbeat(export.Id, lease))
                    {
                        throw new ExportLeaseLostException();
                    }
                }, cancellationToken);
            expiresAt = timeProvider.GetUtcNow() + current.ExportRetention;
            if (!await exports.Complete(export.Id, lease, parts, expiresAt))
            {
                throw new ExportLeaseLostException();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down; the export goes stale and is claimed again.
            throw;
        }
        catch (ExportLeaseLostException)
        {
            logger.LogWarning("Export {ExportId} of account {UserId} was claimed again while this run made it; " +
                "dropping this run's parts", export.Id, export.UserId);
            await storage.DeletePrefixAsync(ExportParts.RunPrefix(export));
            return true;
        }
        catch (Exception error)
        {
            bool retry = export.Attempts < current.ExportMaxAttempts;
            logger.LogError(error, "Export {ExportId} of account {UserId} failed (attempt {Attempt}){Retry}",
                export.Id, export.UserId, export.Attempts, retry ? "; it will be retried" : "");
            await storage.DeletePrefixAsync(ExportParts.RunPrefix(export));
            if (!await exports.Fail(export.Id, lease, error.Message, retry ? current.ExportRetryAfter : null))
            {
                logger.LogWarning("Export {ExportId} was claimed again before its failure was recorded", export.Id);
            }

            return true;
        }

        // Ready: from here nothing may touch its parts or its row, whatever the mail does.
        int clipCount = parts.Sum(part => part.ClipCount);
        logger.LogInformation("Export {ExportId} of account {UserId} is ready: {Clips} clips in {Parts} parts",
            export.Id, export.UserId, clipCount, parts.Count);

        OverLimitState state = await accounts.GetState(export.UserId);
        if (!string.IsNullOrEmpty(state.Email))
        {
            await emailSender.SendAsync(AccountEmails.ExportReady(state.Email, clipCount,
                export.Reason == ClipExportReasons.GraceEnded, expiresAt, _planUrl), cancellationToken);
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

/// <summary>The run's lease on its export is gone: another run claimed it, so this one must stop.</summary>
public sealed class ExportLeaseLostException() : Exception("The export was claimed again by another run");

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
