namespace Reelshelf.Games;

/// <summary>
/// Works out cloth colours for categories that lack one: those created before colours existed, those whose cover
/// changed, and those whose cover could not be read when they were added. Covers are public images, so unlike
/// <see cref="GameCategoryAssetRefreshService"/> this needs no IGDB credentials.
/// </summary>
public class GameCategoryClothColorService(
    ILogger<GameCategoryClothColorService> logger,
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration)
    : BackgroundService
{
    private readonly TimeSpan _interval = TimeSpan.FromHours(
        Math.Max(1, configuration.GetValue("GameCategories:ClothColorIntervalHours", 6)));
    private readonly int _batchSize = Math.Max(1, configuration.GetValue("GameCategories:ClothColorBatchSize", 50));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await FillMissingAsync(stoppingToken);

        using PeriodicTimer timer = new(_interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await FillMissingAsync(stoppingToken);
        }
    }

    private async Task FillMissingAsync(CancellationToken stoppingToken)
    {
        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            GameCategoryStatements statements = scope.ServiceProvider.GetRequiredService<GameCategoryStatements>();
            GameCoverColors coverColors = scope.ServiceProvider.GetRequiredService<GameCoverColors>();

            List<GameCategory> categories = await statements.GetCategoriesNeedingClothColorAsync(_batchSize);
            if (categories.Count == 0) return;

            int updated = 0;
            foreach (GameCategory category in categories)
            {
                if (stoppingToken.IsCancellationRequested) break;

                string? clothColor = await coverColors.TryGetClothColorAsync(category.CoverUrl, stoppingToken);
                if (clothColor == null || category.CoverUrl == null) continue;

                await statements.SetClothColorAsync(category.Id, category.CoverUrl, clothColor);
                updated++;
            }

            logger.LogInformation(
                "Cloth colours worked out for {Updated} of {Count} game categories", updated, categories.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to work out game category cloth colours");
        }
    }
}
