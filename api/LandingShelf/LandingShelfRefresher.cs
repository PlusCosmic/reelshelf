using Microsoft.Extensions.Options;
using Reelshelf.Games;

namespace Reelshelf.LandingShelf;

/// <summary>
/// Fetches the most popular games from IGDB and works out each one's cloth colour from its cover, the same way a
/// library book gets its colour, then replaces <see cref="LandingShelfCache"/>. A failed or empty fetch leaves the
/// previous shelf in place.
/// </summary>
public class LandingShelfRefresher(
    IPopularGameSource source,
    GameCoverColors coverColors,
    LandingShelfCache cache,
    IOptions<LandingShelfOptions> options,
    TimeProvider timeProvider,
    ILogger<LandingShelfRefresher> logger)
{
    /// <summary>True when the cache now holds a fresh shelf.</summary>
    public async Task<bool> RefreshAsync(CancellationToken cancellationToken)
    {
        LandingShelfOptions settings = options.Value;
        IReadOnlyList<PopularGame> popular =
            await source.GetPopularGamesAsync(settings.PopularityType, Math.Max(1, settings.GameCount));
        if (popular.Count == 0)
        {
            logger.LogWarning(
                "IGDB returned no popular games for popularity type {PopularityType}; keeping the current landing shelf",
                settings.PopularityType);
            return false;
        }

        List<LandingShelfGame> games = [];
        foreach (PopularGame game in popular)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? cloth = await coverColors.TryGetClothColorAsync(game.CoverUrl, cancellationToken);
            games.Add(new LandingShelfGame(game.IgdbId, game.Name, game.Slug, game.CoverUrl, cloth));
        }

        cache.Set(new LandingShelfResponse(games, timeProvider.GetUtcNow()));
        logger.LogInformation("Landing shelf refreshed with {Count} popular games", games.Count);
        return true;
    }
}
