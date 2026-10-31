using Microsoft.Extensions.Options;

namespace Reelshelf.LandingShelf;

/// <summary>
/// Refreshes the landing shelf when the API starts and then every <see cref="LandingShelfOptions.RefreshInterval"/>,
/// retrying after <see cref="LandingShelfOptions.RetryAfter"/> when a refresh fails. Never runs in <c>OpenApi</c>.
/// </summary>
public class LandingShelfRefreshService(
    IServiceScopeFactory scopeFactory,
    IOptions<LandingShelfOptions> options,
    ILogger<LandingShelfRefreshService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            bool refreshed = false;
            try
            {
                using IServiceScope scope = scopeFactory.CreateScope();
                refreshed = await scope.ServiceProvider.GetRequiredService<LandingShelfRefresher>()
                    .RefreshAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to refresh the landing shelf");
            }

            TimeSpan wait = refreshed ? options.Value.RefreshInterval : options.Value.RetryAfter;
            await Task.Delay(wait > TimeSpan.Zero ? wait : TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
