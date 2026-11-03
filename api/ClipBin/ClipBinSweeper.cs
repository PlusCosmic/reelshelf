using Microsoft.Extensions.Options;
using Reelshelf.Core;
using Reelshelf.Plans;

namespace Reelshelf.ClipBin;

/// <summary>
/// Deletes for good every clip that has been in the bin longer than <see cref="ClipBinOptions.Retention"/>, the way its
/// owner deleting it would (<see cref="IOwnedClipDeleter"/>). A clip held for review stays until it can go.
/// </summary>
public sealed class ClipBinSweeper(
    ClipsStatements clips,
    IOwnedClipDeleter deleter,
    IOptionsMonitor<ClipBinOptions> options,
    TimeProvider timeProvider,
    ILogger<ClipBinSweeper> logger)
{
    private const int BatchSize = 100;

    public async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset binnedBefore = timeProvider.GetUtcNow() - options.CurrentValue.Retention;
        HashSet<Guid> seen = [];
        int deleted = 0;
        while (true)
        {
            // Clips tried already this pass (held, or failed at their host) are skipped, so the pass ends.
            List<ClipsStatements.BinnedClipRow> due = await clips.GetClipsDueFromBin(binnedBefore, BatchSize, seen);
            if (due.Count == 0)
            {
                break;
            }

            foreach (ClipsStatements.BinnedClipRow clip in due)
            {
                cancellationToken.ThrowIfCancellationRequested();
                seen.Add(clip.Id);
                try
                {
                    if (await deleter.DeleteAsync(clip.Id, clip.OwnerId))
                    {
                        deleted++;
                    }
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    logger.LogError(error, "Couldn't empty clip {ClipId} out of the bin; it is tried again next time",
                        clip.Id);
                }
            }
        }

        if (deleted > 0)
        {
            logger.LogInformation("Emptied {Count} clips out of the bin", deleted);
        }

        return deleted;
    }
}

/// <summary>Runs <see cref="ClipBinSweeper"/> every <see cref="ClipBinOptions.SweepInterval"/>; never in <c>OpenApi</c>.</summary>
public sealed class ClipBinSweepService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<ClipBinOptions> options,
    ILogger<ClipBinSweepService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // A minute after startup, so a deploy's restart doesn't sweep before the rest of the app is up.
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            if (options.CurrentValue.SweepEnabled)
            {
                try
                {
                    await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<ClipBinSweeper>().SweepAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception error)
                {
                    logger.LogError(error, "The bin sweep failed");
                }
            }

            await Task.Delay(options.CurrentValue.SweepInterval, stoppingToken);
        }
    }
}
