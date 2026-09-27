using Microsoft.Extensions.Options;

namespace Reelshelf.ApexLegends.LegendDetection;

/// <summary>
/// Works through queued legend detection runs, <see cref="LegendDetectionOptions.Concurrency"/> at a time, each
/// worker checking for new ones every few seconds once the queue is empty. Runs live in Postgres and are claimed
/// with SKIP LOCKED, so workers never share a run and nothing is lost when the API restarts mid-run.
/// </summary>
public class LegendDetectionBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<LegendDetectionOptions> options,
    ILogger<LegendDetectionBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(10);

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int workers = Math.Max(1, options.Value.Concurrency);
        logger.LogInformation("Processing legend detection runs with {Workers} workers", workers);
        return Task.WhenAll(Enumerable.Range(0, workers).Select(_ => WorkAsync(stoppingToken)));
    }

    private async Task WorkAsync(CancellationToken stoppingToken)
    {
        // Leave ExecuteAsync first, so the workers don't hold up the rest of startup.
        await Task.Yield();
        while (!stoppingToken.IsCancellationRequested)
        {
            bool processed = false;
            try
            {
                using IServiceScope scope = scopeFactory.CreateScope();
                LegendDetectionService service = scope.ServiceProvider.GetRequiredService<LegendDetectionService>();
                processed = await service.ProcessNextRunAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Failed to process legend detection runs");
            }

            if (!processed)
            {
                await Task.Delay(IdleDelay, stoppingToken);
            }
        }
    }
}
