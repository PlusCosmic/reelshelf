namespace Reelshelf.Games;

/// <summary>
/// Keeps IGDB categories' images current. Every <c>GameCategories:AssetRefreshTickMinutes</c> (60) it reads again from
/// IGDB each category on someone's shelf last read more than <c>GameCategories:AssetRefreshStaleAfterHours</c> (24)
/// ago, so a game's new cover, key art or logo (a big update often brings one) reaches the shelf within a day, and
/// copies any image we don't hold yet into our own storage (<see cref="GameArtMirror"/>). IGDB's API terms allow and
/// prefer serving stored copies. Categories go in batches of <c>GameCategories:AssetRefreshBatchSize</c> (50), each
/// IGDB read <c>GameCategories:AssetRefreshDelayMilliseconds</c> (1500) apart, since a read is four IGDB requests and
/// the application's 4 a second are shared with every user's game search.
/// </summary>
public class GameCategoryAssetRefreshService(
    ILogger<GameCategoryAssetRefreshService> logger,
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration)
    : BackgroundService
{
    private readonly TimeSpan _tick = TimeSpan.FromMinutes(
        Math.Max(1, configuration.GetValue("GameCategories:AssetRefreshTickMinutes", 60)));
    private readonly TimeSpan _staleAfter = TimeSpan.FromHours(
        Math.Max(1, configuration.GetValue("GameCategories:AssetRefreshStaleAfterHours", 24)));
    private readonly int _batchSize = Math.Max(1, configuration.GetValue("GameCategories:AssetRefreshBatchSize", 50));
    private readonly TimeSpan _delay = TimeSpan.FromMilliseconds(
        Math.Max(0, configuration.GetValue("GameCategories:AssetRefreshDelayMilliseconds", 1500)));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(configuration["TwitchClientId"]) ||
            string.IsNullOrWhiteSpace(configuration["TwitchClientSecret"]))
        {
            logger.LogWarning("Twitch credentials not configured - game category asset refresh service disabled");
            return;
        }

        await RefreshDueAsync(stoppingToken);

        using PeriodicTimer timer = new(_tick);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RefreshDueAsync(stoppingToken);
        }
    }

    private async Task RefreshDueAsync(CancellationToken stoppingToken)
    {
        int refreshed = 0;
        int copied = 0;
        int failed = 0;
        // A category that fails stays due; remember it so this pass doesn't pick it again.
        HashSet<Guid> seen = [];

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using IServiceScope scope = scopeFactory.CreateScope();
                GameCategoryStatements statements = scope.ServiceProvider.GetRequiredService<GameCategoryStatements>();
                IgdbService igdbService = scope.ServiceProvider.GetRequiredService<IgdbService>();
                GameArtMirror mirror = scope.ServiceProvider.GetRequiredService<GameArtMirror>();

                List<GameCategory> due = (await statements.GetCategoriesNeedingIgdbAssetRefreshAsync(
                        _batchSize + seen.Count, _staleAfter, mirror.IsConfigured))
                    .Where(category => seen.Add(category.Id))
                    .Take(_batchSize)
                    .ToList();
                if (due.Count == 0) break;

                foreach (GameCategory category in due)
                {
                    if (stoppingToken.IsCancellationRequested) break;

                    try
                    {
                        GameCategory current = category;
                        if (IsStale(category) && category.IgdbId is { } igdbId)
                        {
                            GameDetails? details = await igdbService.GetGameByIdAsync(igdbId, refresh: true);
                            if (details is null)
                            {
                                logger.LogWarning(
                                    "IGDB game {IgdbId} not found while refreshing category {CategoryId}",
                                    igdbId, category.Id);
                                await statements.MarkIgdbCheckedAsync(category.Id);
                            }
                            else
                            {
                                current = await statements.UpdateIgdbAssetsAsync(category.Id, details);
                                refreshed++;
                            }

                            await Task.Delay(_delay, stoppingToken);
                        }

                        if (await mirror.CopyAsync(current, stoppingToken)) copied++;
                        else failed++;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogError(ex, "Failed to refresh IGDB assets for category {CategoryId} ({CategoryName})",
                            category.Id, category.Name);
                        failed++;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to refresh game category IGDB assets");
        }

        if (seen.Count > 0)
        {
            logger.LogInformation(
                "Game category assets: {Refreshed} read from IGDB, {Copied} checked or copied, {Failed} failed",
                refreshed, copied, failed);
        }
    }

    private bool IsStale(GameCategory category) =>
        category.IgdbCheckedAt is not { } checkedAt || checkedAt <= DateTimeOffset.UtcNow - _staleAfter;
}
