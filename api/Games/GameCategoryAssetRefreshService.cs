using Reelshelf.Shared.Games;

namespace Reelshelf.Games;

public class GameCategoryAssetRefreshService(
    ILogger<GameCategoryAssetRefreshService> logger,
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration)
    : BackgroundService
{
    private readonly TimeSpan _refreshInterval = TimeSpan.FromHours(
        Math.Max(1, configuration.GetValue("GameCategories:AssetRefreshIntervalHours", 24)));
    private readonly TimeSpan _staleAfter = TimeSpan.FromHours(
        Math.Max(1, configuration.GetValue("GameCategories:AssetRefreshStaleAfterHours", 24)));
    private readonly int _batchSize = Math.Max(1, configuration.GetValue("GameCategories:AssetRefreshBatchSize", 25));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(configuration["IgdbClientId"]) ||
            string.IsNullOrWhiteSpace(configuration["IgdbClientSecret"]))
        {
            logger.LogWarning("IGDB credentials not configured - game category asset refresh service disabled");
            return;
        }

        await RefreshMissingAssetsAsync(stoppingToken);

        using PeriodicTimer timer = new(_refreshInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RefreshMissingAssetsAsync(stoppingToken);
        }
    }

    private async Task RefreshMissingAssetsAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Refreshing missing game category IGDB assets");

        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            GameCategoryStatements statements = scope.ServiceProvider.GetRequiredService<GameCategoryStatements>();
            IgdbService igdbService = scope.ServiceProvider.GetRequiredService<IgdbService>();

            List<GameCategory> categories =
                await statements.GetCategoriesNeedingIgdbAssetRefreshAsync(_batchSize, _staleAfter);

            if (categories.Count == 0)
            {
                logger.LogInformation("No game categories need IGDB asset refresh");
                return;
            }

            logger.LogInformation("Found {Count} game categories needing IGDB asset refresh", categories.Count);

            int updated = 0;
            int failed = 0;

            foreach (GameCategory category in categories)
            {
                if (stoppingToken.IsCancellationRequested) break;

                try
                {
                    if (category.IgdbId is not { } igdbId)
                    {
                        continue;
                    }

                    GameDetails? gameDetails = await igdbService.GetGameByIdAsync(igdbId);
                    if (gameDetails == null)
                    {
                        logger.LogWarning(
                            "IGDB game {IgdbId} not found while refreshing category {CategoryId}",
                            igdbId,
                            category.Id);
                        failed++;
                        continue;
                    }

                    GameCategory refreshed = await statements.UpdateIgdbAssetsAsync(category.Id, gameDetails);
                    if (refreshed.KeyArtUrl != null || refreshed.GameLogoUrl != null)
                    {
                        updated++;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Failed to refresh IGDB assets for category {CategoryId} ({CategoryName})",
                        category.Id,
                        category.Name);
                    failed++;
                }
            }

            logger.LogInformation(
                "Game category IGDB asset refresh complete: {Updated} updated, {Failed} failed",
                updated,
                failed);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to refresh game category IGDB assets");
        }
    }
}
