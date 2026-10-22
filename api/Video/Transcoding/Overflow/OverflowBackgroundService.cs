using Microsoft.Extensions.Options;

namespace Reelshelf.Video.Transcoding.Overflow;

/// <summary>
/// Runs <see cref="OverflowScaler"/> every <see cref="OverflowOptions.Interval"/>. Registered only where
/// <c>Overflow:Enabled</c> is true, and never in the <c>OpenApi</c> environment (<see cref="OverflowSetup"/>).
/// </summary>
public sealed class OverflowBackgroundService(
    OverflowScaler scaler,
    IOptions<OverflowOptions> options,
    ILogger<OverflowBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Leave ExecuteAsync first, so the first tick doesn't hold up the rest of startup.
        await Task.Yield();
        OverflowOptions current = options.Value;
        logger.LogInformation(
            "Overflow pool on: checking every {Interval}, up to {MaxTasks} Batch workers in {Region}",
            current.EffectiveInterval, current.MaxTasks, current.Batch.Region);

        using PeriodicTimer timer = new(current.EffectiveInterval);
        do
        {
            await scaler.TickAsync(stoppingToken);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
