using Microsoft.Extensions.Options;
using Reelshelf.Billing;
using Reelshelf.Core;
using Reelshelf.Email;

namespace Reelshelf.Plans;

/// <summary>An account's plan by id; <see cref="AccountPlanService"/>.</summary>
public interface IAccountPlans
{
    Task<AccountPlan> GetPlan(Guid userId);
}

/// <summary>Deletes an owner's clip the way the owner deleting it would; <see cref="ClipService.DeleteClip"/>.</summary>
public interface IOwnedClipDeleter
{
    Task<bool> DeleteAsync(Guid clipId, Guid ownerId);
}

public sealed class ClipServiceDeleter(ClipService clips) : IOwnedClipDeleter
{
    public Task<bool> DeleteAsync(Guid clipId, Guid ownerId) => clips.DeleteClip(clipId, ownerId);
}

/// <summary>
/// Going over the limit (ADR-0007), once per <see cref="PlanOptions.SweepInterval"/>. An account with more clips than
/// its tier keeps starts a grace (<c>app_user.over_limit_since</c>) and is mailed; <see cref="PlanOptions.GraceWarningBefore"/>
/// its end it is mailed again; when the grace ends its clips over the limit are exported, and deleted once the export is
/// ready. An account back under its limit by any route (fewer clips, a better tier) ends its grace. Clips the owner
/// picked are kept first, then the newest (<see cref="OverLimit.Split"/>).
/// </summary>
public sealed class OverLimitSweeper(
    IPlanEnforcementStore accounts,
    IAccountPlans plans,
    IClipExportStore exports,
    IOwnedClipDeleter deleter,
    IEmailSender emailSender,
    IOptionsMonitor<PlanOptions> options,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<OverLimitSweeper> logger)
{
    private readonly string _planUrl = ClipExportProcessor.PlanUrl(configuration);

    public async Task SweepAsync(CancellationToken cancellationToken)
    {
        int smallestLimit = TierLimits.For(AccountTier.Free).MaxClips!.Value;
        foreach (Guid userId in await accounts.GetAccountsToCheck(smallestLimit))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await SweepAccountAsync(userId, cancellationToken);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                logger.LogError(error, "The over-limit sweep failed for account {UserId}", userId);
            }
        }
    }

    public async Task SweepAccountAsync(Guid userId, CancellationToken cancellationToken)
    {
        PlanOptions current = options.CurrentValue;
        DateTimeOffset now = timeProvider.GetUtcNow();
        AccountPlan plan = await plans.GetPlan(userId);
        OverLimitState state = await accounts.GetState(userId);

        if (plan.Limits.MaxClips is not { } limit || plan.ClipCount <= limit)
        {
            if (state.OverLimitSince is not null)
            {
                await accounts.EndGrace(userId);
                logger.LogInformation("Account {UserId} is back within its {Tier} plan; its grace ended", userId, plan.Tier);
            }

            return;
        }

        (_, List<OwnedClip> over) = OverLimit.Split(await accounts.GetOwnedClips(userId), limit);
        if (state.OverLimitSince is not { } since)
        {
            if (await accounts.StartGrace(userId))
            {
                logger.LogInformation("Account {UserId} has {Over} clips over its {Tier} plan's limit; its grace started",
                    userId, over.Count, plan.Tier);
                await MailAsync(state.Email,
                    email => AccountEmails.OverLimitStarted(email, over.Count, now + current.GracePeriod, _planUrl),
                    cancellationToken);
            }

            return;
        }

        DateTimeOffset deletesAt = since + current.GracePeriod;
        if (now < deletesAt)
        {
            if (now >= deletesAt - current.GraceWarningBefore && state.WarnedAt is null &&
                await accounts.MarkGraceWarned(userId))
            {
                await MailAsync(state.Email,
                    email => AccountEmails.OverLimitEndingSoon(email, over.Count, deletesAt, _planUrl),
                    cancellationToken);
            }

            return;
        }

        await EndGraceAsync(userId, since, over, cancellationToken);
    }

    /// <summary>
    /// The grace is over: export the clips over the limit, then, once that export is ready, delete the ones it holds
    /// that are still over. A failed export is left for a person; nothing is deleted without one.
    /// </summary>
    private async Task EndGraceAsync(Guid userId, DateTimeOffset since, List<OwnedClip> over,
        CancellationToken cancellationToken)
    {
        ClipExport? export = (await exports.GetForUser(userId))
            .Where(candidate => candidate.Reason == ClipExportReasons.GraceEnded && candidate.CreatedAt >= since)
            .MaxBy(candidate => candidate.CreatedAt);

        if (export is null || export.Status == ClipExportStatuses.Expired)
        {
            await exports.Create(userId, ClipExportReasons.GraceEnded, over.Select(clip => clip.Id).ToList());
            logger.LogInformation("Account {UserId}'s grace ended; exporting its {Over} clips over the limit",
                userId, over.Count);
            return;
        }

        if (export.Status == ClipExportStatuses.Failed)
        {
            logger.LogError("Account {UserId}'s grace ended but export {ExportId} failed ({Error}); nothing is deleted",
                userId, export.Id, export.Error);
            return;
        }

        if (export.Status != ClipExportStatuses.Ready)
        {
            return;
        }

        HashSet<Guid> exported = export.ClipIds.ToHashSet();
        List<OwnedClip> toDelete = over.Where(clip => exported.Contains(clip.Id)).ToList();
        int deleted = 0;
        foreach (OwnedClip clip in toDelete)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await deleter.DeleteAsync(clip.Id, userId))
            {
                deleted++;
            }
        }

        logger.LogInformation("Account {UserId}'s grace ended; deleted {Deleted} clips over the limit (export {ExportId})",
            userId, deleted, export.Id);

        // Clips the owner swapped in after the export was made are still over; the next pass exports those.
        if (over.Count == toDelete.Count)
        {
            await accounts.EndGrace(userId);
        }
        else if (toDelete.Count == 0)
        {
            await exports.Create(userId, ClipExportReasons.GraceEnded,
                over.Select(clip => clip.Id).ToList());
        }
    }

    private async Task MailAsync(string? to, Func<string, EmailMessage> message, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(to))
        {
            await emailSender.SendAsync(message(to), cancellationToken);
        }
    }
}

/// <summary>Runs <see cref="OverLimitSweeper"/> where <see cref="PlanOptions.Enforce"/> is on. Never in <c>OpenApi</c>.</summary>
public sealed class OverLimitSweepService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<PlanOptions> options,
    ILogger<OverLimitSweepService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // A minute after startup, so a deploy's restart doesn't sweep before the rest of the app is up.
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            if (options.CurrentValue.Enforce)
            {
                try
                {
                    await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<OverLimitSweeper>().SweepAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception error)
                {
                    logger.LogError(error, "The over-limit sweep failed");
                }
            }

            await Task.Delay(options.CurrentValue.SweepInterval, stoppingToken);
        }
    }
}
