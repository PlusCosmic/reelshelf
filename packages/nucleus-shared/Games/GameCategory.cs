namespace Nucleus.Shared.Games;

public class GameCategory
{
    public Guid Id { get; init; }
    public long? IgdbId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string? CoverUrl { get; init; }
    public string? KeyArtUrl { get; init; }
    public string? GameLogoUrl { get; init; }
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
    bool IsCustom
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
