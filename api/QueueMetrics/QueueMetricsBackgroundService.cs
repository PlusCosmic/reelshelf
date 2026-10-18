using Microsoft.Extensions.Options;

namespace Reelshelf.QueueMetrics;

/// <summary>
/// Samples every registered <see cref="IRunQueueMetricsSource"/> each <see cref="QueueMetricsOptions.Interval"/>
/// and hands the numbers to <see cref="RunQueueMetrics"/>, whose gauges report them to whatever reads the meter.
/// </summary>
public class QueueMetricsBackgroundService(
    IEnumerable<IRunQueueMetricsSource> sources,
    RunQueueMetrics metrics,
    IOptions<QueueMetricsOptions> options,
    ILogger<QueueMetricsBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Leave ExecuteAsync first, so sampling doesn't hold up the rest of startup.
        await Task.Yield();
        List<IRunQueueMetricsSource> queues = sources.ToList();
        TimeSpan interval = options.Value.Interval < MinimumInterval ? MinimumInterval : options.Value.Interval;
        logger.LogInformation("Sampling {Count} run queues every {Interval}", queues.Count, interval);

        using PeriodicTimer timer = new(interval);
        do
        {
            await SampleAsync(queues, stoppingToken);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task SampleAsync(IReadOnlyList<IRunQueueMetricsSource> queues, CancellationToken stoppingToken)
    {
        foreach (IRunQueueMetricsSource source in queues)
        {
            try
            {
                metrics.Publish(source.Queue, await source.SampleAsync(stoppingToken));
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                metrics.Clear(source.Queue);
                logger.LogWarning(ex, "Failed to sample the {Queue} queue", source.Queue);
            }
        }
    }
}
