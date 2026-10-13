using Reelshelf.Bunny;
using Reelshelf.Bunny.Models;

namespace Reelshelf.Core;

/// <summary>
/// Folds duplicate Bunny collections set aside by V36 into the owner's collection for the category: moves every
/// video Bunny lists in the duplicate, then deletes the duplicate once it is empty. Each step is idempotent, so a
/// pass that fails part-way is finished by a later one.
/// </summary>
public class ClipCollectionMergeService(
    ILogger<ClipCollectionMergeService> logger,
    IServiceScopeFactory scopeFactory)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private const int BatchSize = 20;
    private const int PageSize = 100;
    // Bounds a pass over one collection in case Bunny keeps listing a video it was asked to move.
    private const int MaxRounds = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await MergePendingAsync(stoppingToken);

        using PeriodicTimer timer = new(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await MergePendingAsync(stoppingToken);
        }
    }

    private async Task MergePendingAsync(CancellationToken stoppingToken)
    {
        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            ClipsStatements statements = scope.ServiceProvider.GetRequiredService<ClipsStatements>();
            BunnyService bunnyService = scope.ServiceProvider.GetRequiredService<BunnyService>();

            foreach (ClipsStatements.ClipCollectionMergeRow merge in await statements.GetPendingCollectionMerges(BatchSize))
            {
                if (stoppingToken.IsCancellationRequested) break;

                try
                {
                    if (await MoveVideosAsync(bunnyService, merge))
                    {
                        await bunnyService.DeleteCollectionAsync(merge.CollectionId);
                        await statements.DeleteCollectionMerge(merge.CollectionId);
                        logger.LogInformation(
                            "Merged Bunny collection {CollectionId} into {TargetCollectionId}",
                            merge.CollectionId, merge.TargetCollectionId);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex,
                        "Failed to merge Bunny collection {CollectionId} into {TargetCollectionId}",
                        merge.CollectionId, merge.TargetCollectionId);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to merge duplicate clip collections");
        }
    }

    /// <summary>True once Bunny lists no videos left in the duplicate collection.</summary>
    private async Task<bool> MoveVideosAsync(BunnyService bunnyService, ClipsStatements.ClipCollectionMergeRow merge)
    {
        for (int round = 0; round < MaxRounds; round++)
        {
            // Moved videos leave the listing, so the first page always holds whatever is still to move.
            PagedVideoResponse page = await bunnyService.GetVideosForCollectionAsync(merge.CollectionId, 1, PageSize);
            if (page.Items.Count == 0)
            {
                return true;
            }

            // Never move a video the filter let through by mistake: that would pull it out of another collection.
            if (page.Items.Any(video => video.CollectionId != merge.CollectionId))
            {
                throw new InvalidOperationException(
                    $"Bunny listed videos outside collection {merge.CollectionId} when filtering by it");
            }

            foreach (BunnyVideo video in page.Items)
            {
                await bunnyService.MoveVideoToCollectionAsync(video.Guid, merge.TargetCollectionId);
            }
        }

        logger.LogWarning(
            "Bunny collection {CollectionId} still lists videos after {Rounds} rounds of moves; leaving it for the next pass",
            merge.CollectionId, MaxRounds);
        return false;
    }
}
