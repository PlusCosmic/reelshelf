using System.Collections.Concurrent;
using Reelshelf.Email;

namespace Reelshelf.Users;

/// <summary>
/// Deletes an account and everything in it. A request marks the account and removes its sign-in identities at
/// once, so it can't be used again; <see cref="AccountDeletionBackgroundService"/> then deletes its Bunny videos
/// and collections and finally the account row, which cascades to the rest of its data. Every step is idempotent,
/// so a pass that fails part-way is simply repeated.
/// </summary>
public class AccountDeletionService(
    IAccountDeletionStore store,
    IClipVideoHost videoHost,
    IEmailSender emailSender,
    AccountDeletionSignal signal,
    ILogger<AccountDeletionService> logger)
{
    /// <summary>What the user types to confirm; checked here as well as in the UI so a stray request can't delete an account.</summary>
    public const string ConfirmationPhrase = "delete my account";

    private const int MaxConcurrentVideoDeletes = 4;

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
        List<OwnedClipVideo> clips = await store.GetClipVideos(userId);
        ConcurrentBag<Guid> deletedClipIds = [];
        bool videosFailed = false;

        await Parallel.ForEachAsync(
            clips,
            new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentVideoDeletes, CancellationToken = cancellationToken },
            async (clip, _) =>
            {
                try
                {
                    await videoHost.DeleteVideoAsync(clip.VideoId);
                    deletedClipIds.Add(clip.ClipId);
                }
                catch (Exception ex)
                {
                    videosFailed = true;
                    logger.LogError(ex, "Failed to delete Bunny video {VideoId} of clip {ClipId} for deleted account {UserId}",
                        clip.VideoId, clip.ClipId, userId);
                }
            });

        if (!deletedClipIds.IsEmpty)
        {
            await store.DeleteClips(userId, deletedClipIds.ToList());
        }

        if (videosFailed)
        {
            return false;
        }

        // The collection ids are only kept on rows that go with the account, so collections are removed first.
        foreach (Guid collectionId in await store.GetBunnyCollections(userId))
        {
            try
            {
                await videoHost.DeleteCollectionAsync(collectionId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to delete Bunny collection {CollectionId} for deleted account {UserId}", collectionId, userId);
                return false;
            }
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
}

public sealed record AccountDeletionRequest(string? Email);

public sealed record OwnedClipVideo(Guid ClipId, Guid VideoId);

public interface IAccountDeletionStore
{
    /// <summary>
    /// Marks the account and deletes its identities (and with them any stored provider tokens) in one transaction.
    /// Null when the account doesn't exist or is already marked.
    /// </summary>
    Task<AccountDeletionRequest?> MarkForDeletion(Guid userId);

    Task<List<Guid>> GetAccountsPendingDeletion();
    Task<List<OwnedClipVideo>> GetClipVideos(Guid userId);
    Task DeleteClips(Guid userId, IReadOnlyCollection<Guid> clipIds);
    Task<List<Guid>> GetBunnyCollections(Guid userId);

    /// <summary>Deletes the marked account row only if it owns no clips. True when it was deleted.</summary>
    Task<bool> DeleteAccountIfEmpty(Guid userId);
}

/// <summary>The video host operations account deletion needs; implemented by <see cref="Bunny.BunnyService"/>.</summary>
public interface IClipVideoHost
{
    Task DeleteVideoAsync(Guid videoId);
    Task DeleteCollectionAsync(Guid collectionId);
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

public class AccountDeletionBackgroundService(
    ILogger<AccountDeletionBackgroundService> logger,
    IServiceScopeFactory scopeFactory,
    AccountDeletionSignal signal)
    : BackgroundService
{
    /// <summary>How often marked accounts are retried when nothing wakes the service.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
