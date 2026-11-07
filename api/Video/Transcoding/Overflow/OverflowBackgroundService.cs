using Microsoft.Extensions.Options;

namespace Reelshelf.Video.Transcoding.Overflow;

/// <summary>
/// Runs <see cref="OverflowScaler"/> every <see cref="OverflowOptions.Interval"/>. Registered only where
/// <c>Overflow:Enabled</c> is true, and never in the <c>OpenApi</c> environment (<see cref="OverflowSetup"/>). Each
/// tick runs under <see cref="IOverflowTickLock"/>, so while a deploy runs two instances only one of them scales.
/// </summary>
public sealed class OverflowBackgroundService(
    OverflowScaler scaler,
    IOverflowTickLock tickLock,
    IOptions<OverflowOptions> options,
    ILogger<OverflowBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Leave ExecuteAsync first, so the first tick doesn't hold up the rest of startup.
        await Task.Yield();
        OverflowOptions current = options.Value;
        logger.LogInformation(
            "Overflow pool on: checking every {Interval}, up to {MaxTasks} workers in {Pools}",
            current.EffectiveInterval, current.MaxTasks,
            string.Join(", then ", current.PoolList.Select(pool => $"{pool} (up to {current.MaxTasksFor(pool)})")));

        using PeriodicTimer timer = new(current.EffectiveInterval);
        do
        {
            try
            {
                if (!await tickLock.TryRunAsync(async token => await scaler.TickAsync(token), stoppingToken))
                {
                    logger.LogDebug("Another instance is running the overflow tick; skipping this one");
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Overflow tick failed; trying again on the next one");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
