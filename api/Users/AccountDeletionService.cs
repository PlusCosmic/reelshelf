using System.Collections.Concurrent;
using Reelshelf.Billing;
using Reelshelf.Email;
using Reelshelf.FeatureFlags;
using Reelshelf.Plans;
using Reelshelf.Video;
using Reelshelf.Video.Transcoding;

namespace Reelshelf.Users;

/// <summary>
/// Deletes an account and everything in it. A request marks the account and removes its sign-in identities at
/// once, so it can't be used again; <see cref="AccountDeletionBackgroundService"/> then cancels its Stripe
/// subscriptions, deletes its videos at their host, and finally the account row, which cascades to the
/// rest of its data. Every step is idempotent, so a pass that fails part-way is simply repeated. While the account is
/// held for review (<see cref="IAccountDeletionStore.IsHeldForReview"/>: a child-safety or terrorism report on one of
/// its clips is open, or a removed clip's video hasn't been copied as evidence yet) a pass only cancels the
/// subscriptions and deletes nothing, so a report filed after deletion was requested still keeps the evidence; the
/// account waits, pass after pass, until an admin has dealt with the report.
/// </summary>
public class AccountDeletionService(
    IAccountDeletionStore store,
    ClipVideoHosts videoHosts,
    IEmailSender emailSender,
    AccountDeletionSignal signal,
    IAccountSubscriptions subscriptions,
    IExportStorage exports,
    IPostHogPeople posthog,
    ILogger<AccountDeletionService> logger)
{
    /// <summary>What the user types to confirm; checked here as well as in the UI so a stray request can't delete an account.</summary>
    public const string ConfirmationPhrase = "delete my account";

    private const int MaxConcurrentVideoDeletes = 4;

    /// <summary>Clips whose videos are deleted between two checks of the hold, so a report filed mid-pass stops it soon.</summary>
    private const int VideoDeleteBatchSize = MaxConcurrentVideoDeletes * 5;

    public static bool IsConfirmed(string? confirmation)
    {
        return string.Equals(confirmation?.Trim(), ConfirmationPhrase, StringComparison.OrdinalIgnoreCase);
    }

    public async Task Request(Guid userId)
    {
        AccountDeletionRequest? request = await store.MarkForDeletion(userId);
        if (request is null)
        {
            // Already being deleted.
            return;
        }

        logger.LogInformation("Account {UserId} asked to be deleted", userId);
        if (!string.IsNullOrEmpty(request.Email))
        {
            await emailSender.SendAsync(AccountEmails.AccountDeletionStarted(request.Email));
        }

        signal.Wake();
    }

    /// <summary>Removes what it can of one marked account. Returns true once the account row itself is gone.</summary>
    public async Task<bool> Process(Guid userId, CancellationToken cancellationToken)
    {
        // First, so a deleted account stops being billed even while its videos take a while to go.
        try
        {
            await subscriptions.CancelAllAsync(userId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to cancel the Stripe subscriptions of deleted account {UserId}", userId);
            return false;
        }

        if (await WaitsOnReview(userId))
        {
            return false;
        }

        List<OwnedClipVideo> clips = await store.GetClipVideos(userId);
        ConcurrentBag<Guid> deletedClipIds = [];
        bool videosFailed = false;
        bool held = false;

        foreach (OwnedClipVideo[] batch in clips.Chunk(VideoDeleteBatchSize))
        {
            // Checked again before each batch, as a report can be filed while the videos go. The check runs between
            // batches because the store's connection can't be shared with the parallel deletes.
            if (!deletedClipIds.IsEmpty && await WaitsOnReview(userId))
            {
                held = true;
                break;
            }

            await Parallel.ForEachAsync(
                batch,
                new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentVideoDeletes, CancellationToken = cancellationToken },
                async (clip, _) =>
                {
                    try
                    {
                        await videoHosts.For(clip.VideoHost).DeleteVideoAsync(clip.VideoId);
                        deletedClipIds.Add(clip.ClipId);
                    }
                    catch (Exception ex)
                    {
                        videosFailed = true;
                        logger.LogError(ex, "Failed to delete {VideoHost} video {VideoId} of clip {ClipId} for deleted account {UserId}",
                            clip.VideoHost, clip.VideoId, clip.ClipId, userId);
                    }
                });
        }

        if (!deletedClipIds.IsEmpty)
        {
            // Leaves out a clip under an open urgent report, should one have been filed since the last check.
            await store.DeleteClips(userId, deletedClipIds.ToList());
        }

        if (held)
        {
            return false;
        }

        if (videosFailed)
        {
            return false;
        }

        // Exports are copies of the clips, so they go with them rather than waiting for their expiry.
        try
        {
            await exports.DeletePrefixAsync(ExportParts.AccountPrefix(userId));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete the exports of deleted account {UserId}", userId);
            return false;
        }

        // The account's person at PostHog (ADR-0009), before the row that records it goes with the account.
        try
        {
            await posthog.DeleteAsync(userId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to delete the PostHog person of deleted account {UserId}", userId);
            return false;
        }

        // Refused while any clip remains, e.g. one reserved by an upload already in flight when deletion was
        // requested; the next pass deletes its video first.
        if (!await store.DeleteAccountIfEmpty(userId))
        {
            return false;
        }

        logger.LogInformation("Deleted account {UserId} with {ClipCount} clips", userId, deletedClipIds.Count);
        return true;
    }

    private async Task<bool> WaitsOnReview(Guid userId)
    {
        if (!await store.IsHeldForReview(userId))
        {
            return false;
        }

        logger.LogWarning("Deletion of account {UserId} waits on an admin's review of a child-safety or terrorism report " +
                          "on one of its clips; it is retried on later passes", userId);
        return true;
    }
}

public sealed record AccountDeletionRequest(string? Email);

/// <summary>A clip's video, at the host that holds it.</summary>
public sealed record OwnedClipVideo(Guid ClipId, Guid VideoId, string VideoHost);

public interface IAccountDeletionStore
{
    /// <summary>
    /// Marks the account and deletes its identities (and with them any stored provider tokens) in one transaction.
    /// Null when the account doesn't exist or is already marked.
    /// </summary>
    Task<AccountDeletionRequest?> MarkForDeletion(Guid userId);

    Task<List<Guid>> GetAccountsPendingDeletion();
    Task<List<OwnedClipVideo>> GetClipVideos(Guid userId);

    /// <summary>
    /// Whether the account's videos and rows must stay for now: a child-safety or terrorism report on one of its clips
    /// is open (as in <see cref="Moderation.ContentReportStatements.IsHeldForReview"/>), or a clip removed after one
    /// still waits for its video to be copied as evidence.
    /// </summary>
    Task<bool> IsHeldForReview(Guid userId);

    /// <summary>Deletes the clip rows, leaving out any under an open child-safety or terrorism report.</summary>
    Task DeleteClips(Guid userId, IReadOnlyCollection<Guid> clipIds);

    /// <summary>
    /// Deletes the marked account row only if it owns no clips and no clip of its removed as evidence still waits to
    /// be copied. True when it was deleted.
    /// </summary>
    Task<bool> DeleteAccountIfEmpty(Guid userId);
}

/// <summary>Lets a deletion request start the background pass straight away instead of waiting for the next poll.</summary>
public sealed class AccountDeletionSignal
{
    private readonly SemaphoreSlim _wake = new(0, 1);

    public void Wake()
    {
        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // A wake-up is already pending.
        }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        return _wake.WaitAsync(timeout, cancellationToken);
    }
}

/// <summary>
/// Runs only where <c>AccountDeletion:Enabled</c> is true (the default). Dev shares prod's database, so it sets it to
/// false and leaves deleting accounts, whichever instance they were requested on, to prod.
/// </summary>
public class AccountDeletionBackgroundService(
    ILogger<AccountDeletionBackgroundService> logger,
    IServiceScopeFactory scopeFactory,
    AccountDeletionSignal signal,
    IConfiguration configuration)
    : BackgroundService
{
    /// <summary>How often marked accounts are retried when nothing wakes the service.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("AccountDeletion:Enabled", true))
        {
            logger.LogInformation("Account deletion is disabled on this instance; another instance deletes requested accounts");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunPassAsync(stoppingToken);
            try
            {
                await signal.WaitAsync(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task RunPassAsync(CancellationToken stoppingToken)
    {
        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            IAccountDeletionStore store = scope.ServiceProvider.GetRequiredService<IAccountDeletionStore>();
            AccountDeletionService service = scope.ServiceProvider.GetRequiredService<AccountDeletionService>();

            foreach (Guid userId in await store.GetAccountsPendingDeletion())
            {
                try
                {
                    await service.Process(userId, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Failed to delete account {UserId}; retrying on the next pass", userId);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to process account deletions");
        }
    }
}
