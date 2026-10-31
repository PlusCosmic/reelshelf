namespace Reelshelf.LandingShelf;

/// <summary>A game ranked by IGDB's popularity data, with its IGDB cover.</summary>
public sealed record PopularGame(long IgdbId, string Name, string Slug, string CoverUrl);

/// <summary>Where the landing page's popular games come from; <see cref="Games.IgdbService"/> in production.</summary>
public interface IPopularGameSource
{
    Task<IReadOnlyList<PopularGame>> GetPopularGamesAsync(int popularityType, int count);
}

/// <summary>One book on the landing page's example shelf.</summary>
public sealed record LandingShelfGame(long IgdbId, string Name, string Slug, string CoverUrl, string? ClothColor);

/// <summary>
/// The games the signed-out landing page fills its example shelf from, most popular first. Empty until the first
/// refresh lands (or when IGDB can't be reached), and the page then keeps its built-in shelf.
/// </summary>
public sealed record LandingShelfResponse(List<LandingShelfGame> Games, DateTimeOffset? UpdatedAt);

/// <summary><c>LandingShelf</c> configuration.</summary>
public sealed class LandingShelfOptions
{
    public const string SectionName = "LandingShelf";

    /// <summary>
    /// The IGDB <c>popularity_types</c> id the games are ranked by. 5 is Steam's 24-hour peak players; 1 is IGDB page
    /// visits, 2 to 4 are IGDB's Want to Play, Playing and Played list additions.
    /// </summary>
    public int PopularityType { get; set; } = 5;

    /// <summary>How many games are cached; the page shows a random handful of them on each visit.</summary>
    public int GameCount { get; set; } = 36;

    /// <summary>How often the list is fetched again. IGDB recomputes its popularity data once a day.</summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromDays(1);

    /// <summary>How soon a failed or empty fetch is tried again.</summary>
    public TimeSpan RetryAfter { get; set; } = TimeSpan.FromMinutes(15);
}

/// <summary>The latest landing shelf, held in memory by the one API instance and replaced whole on each refresh.</summary>
public sealed class LandingShelfCache
{
    private LandingShelfResponse _current = new([], null);

    public LandingShelfResponse Current => Volatile.Read(ref _current);

    public void Set(LandingShelfResponse shelf) => Volatile.Write(ref _current, shelf);
}
