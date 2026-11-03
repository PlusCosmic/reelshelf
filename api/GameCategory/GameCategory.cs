namespace Reelshelf.Games;

public class GameCategory
{
    public Guid Id { get; init; }
    public long? IgdbId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    /// <summary>The cover's source: IGDB's URL for an IGDB game, or whatever a custom category's creator gave.</summary>
    public string? CoverUrl { get; init; }
    public string? KeyArtUrl { get; init; }
    public string? GameLogoUrl { get; init; }
    /// <summary>Our copy of <see cref="CoverUrl"/> in R2 (<see cref="GameArtKeys"/>); null until copied.</summary>
    public string? CoverKey { get; init; }
    public string? KeyArtKey { get; init; }
    public string? GameLogoKey { get; init; }
    /// <summary>When the category was last read from IGDB; null before V56 or for custom categories.</summary>
    public DateTimeOffset? IgdbCheckedAt { get; init; }
    /// <summary>The book's cloth on the library shelf as #rrggbb, from the cover; null until worked out.</summary>
    public string? ClothColor { get; init; }
    public bool IsCustom { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public record CreateGameCategoryRequest(
    long? IgdbId,
    string Name,
    string Slug,
    string? CoverUrl,
    string? KeyArtUrl = null,
    string? GameLogoUrl = null
);

public record GameCategoryResponse(
    Guid Id,
    string Name,
    string Slug,
    string? CoverUrl,
    string? KeyArtUrl,
    string? GameLogoUrl,
    bool IsCustom,
    /// <summary>The book's cloth on the library shelf as #rrggbb; null until worked out, or for custom covers.</summary>
    string? ClothColor
);

public record GameSearchResult(long IgdbId, string Name, string Slug, string? CoverUrl);

public record GameDetails(
    long IgdbId,
    string Name,
    string Slug,
    string? CoverUrl,
    string? KeyArtUrl,
    string? GameLogoUrl,
    List<string> Genres,
    List<string> Platforms,
    string? Summary
);

public record AddGameFromIgdbRequest(long IgdbId);

public record AddCustomCategoryRequest(string Name, string? CoverUrl);

/// <summary>The owner's books, left to right as they should stand on the shelf.</summary>
public record SetShelfOrderRequest(List<Guid> CategoryIds);
