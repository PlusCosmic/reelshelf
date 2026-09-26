namespace Reelshelf.ApexLegends.LegendDetection;

/// <summary>
/// Works through queued legend detection runs one at a time, checking for new ones every few seconds once the
/// queue is empty. Runs live in Postgres, so nothing is lost when the API restarts mid-run.
/// </summary>
public class LegendDetectionBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<LegendDetectionBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
