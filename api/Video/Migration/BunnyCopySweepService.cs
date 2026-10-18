using Microsoft.Extensions.Options;

namespace Reelshelf.Video.Migration;

/// <summary>What one sweep pass did.</summary>
public sealed record BunnyCopySweepResult(int VideosDeleted, int CollectionsDeleted, int Failures);

/// <summary>
/// One pass of the Bunny delete sweep (docs/video-pipeline/bunny-migration.md): deletes, through the Bunny host, the
/// Bunny copy of every clip that moved to R2 longer ago than the grace period and clears its <c>bunny_video_id</c>,
/// then deletes the Bunny collections nothing refers to any more. It works in small batches with a pause between
/// Bunny calls. Bunny's delete counts a video or collection it no longer knows about as deleted; any other error is
/// logged, and that copy or collection is left for the next pass.
/// </summary>
public class BunnyCopySweeper(IBunnyCopyStore store, ClipVideoHosts videoHosts, ILogger<BunnyCopySweeper> logger)
{
    public const int BatchSize = 20;

    /// <summary>The pause between Bunny calls, so a pass deletes a couple of videos a second at most.</summary>
    public TimeSpan Pause { get; init; } = TimeSpan.FromMilliseconds(500);

    public async Task<BunnyCopySweepResult> RunAsync(TimeSpan keepFor, CancellationToken cancellationToken)
    {
        IClipVideoHost bunny = videoHosts.For(VideoHostNames.Bunny);
        int videosDeleted = 0;
        int collectionsDeleted = 0;

        int failures = 0;

        // What failed or was passed over is skipped for the rest of the pass, so it can't hold back the rest.
        HashSet<Guid> skippedClips = [];
        while (!cancellationToken.IsCancellationRequested)
        {
            List<BunnyCopy> batch = await store.GetExpiredCopiesAsync(keepFor, skippedClips, BatchSize);
            if (batch.Count == 0)
            {
                break;
            }

            foreach (BunnyCopy copy in batch)
            {
                try
                {
                    if (!await store.DeleteCopyAsync(copy, keepFor, bunny.DeleteVideoAsync))
                    {
                        // No longer due (switched back, or another instance has it): no Bunny call was made.
                        skippedClips.Add(copy.ClipId);
                        continue;
                    }

                    videosDeleted++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failures++;
                    skippedClips.Add(copy.ClipId);
                    logger.LogError(ex, "Failed to delete Bunny video {BunnyVideoId} of migrated clip {ClipId}; retrying on the next pass",
                        copy.BunnyVideoId, copy.ClipId);
                }

                await Task.Delay(Pause, cancellationToken);
            }
        }

        HashSet<Guid> skippedCollections = [];
        while (!cancellationToken.IsCancellationRequested)
        {
            List<OwnedBunnyCollection> batch = await store.GetUnusedCollectionsAsync(skippedCollections, BatchSize);
            if (batch.Count == 0)
            {
                break;
            }

            foreach (OwnedBunnyCollection collection in batch)
            {
                try
                {
                    if (!await store.DeleteCollectionAsync(collection, bunny.DeleteContainerAsync))
                    {
                        // A clip refers to it again (an upload reserved meanwhile).
                        skippedCollections.Add(collection.Id);
                        continue;
                    }

                    collectionsDeleted++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failures++;
                    skippedCollections.Add(collection.Id);
                    logger.LogError(ex, "Failed to delete Bunny collection {CollectionId} of owner {OwnerId}; retrying on the next pass",
                        collection.CollectionId, collection.OwnerId);
                }

                await Task.Delay(Pause, cancellationToken);
            }
        }

        if (videosDeleted > 0 || collectionsDeleted > 0)
        {
            logger.LogInformation("Deleted {VideoCount} Bunny copies of migrated clips and {CollectionCount} unused Bunny collections",
                videosDeleted, collectionsDeleted);
        }

        return new BunnyCopySweepResult(videosDeleted, collectionsDeleted, failures);
    }
}

/// <summary>
/// Runs <see cref="BunnyCopySweeper"/> every <see cref="PassInterval"/> while <see cref="MigrationOptions.DeleteBunny"/>
/// is on; it's read on every pass, so turning it on or off needs no restart. Never registered in the <c>OpenApi</c>
/// environment.
/// </summary>
public class BunnyCopySweepService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<MigrationOptions> options,
    ILogger<BunnyCopySweepService> logger)
    : BackgroundService
{
    /// <summary>The grace period is days long, so an hour late is nothing.</summary>
    private static readonly TimeSpan PassInterval = TimeSpan.FromHours(1);

    private bool _warnedKeepingCopies;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(PassInterval);
        do
        {
            await RunPassAsync(stoppingToken);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task<BunnyCopySweepResult?> RunPassAsync(CancellationToken stoppingToken)
    {
        MigrationOptions current = options.CurrentValue;
        if (!current.DeleteBunny)
        {
            if (current.Enabled && !_warnedKeepingCopies)
            {
                // The privacy policy says a moved clip's Bunny copy is deleted after 7 days.
                logger.LogWarning("Migration:Enabled is on but Migration:DeleteBunny is off; moved clips' Bunny copies are kept until it is turned on");
                _warnedKeepingCopies = true;
            }

            return null;
        }

        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            BunnyCopySweeper sweeper = scope.ServiceProvider.GetRequiredService<BunnyCopySweeper>();
            return await sweeper.RunAsync(current.EffectiveDeleteBunnyAfter, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to sweep Bunny copies of migrated clips");
            return null;
        }
    }
}
