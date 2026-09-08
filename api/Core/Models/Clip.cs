using Reelshelf.Bunny.Models;
using Reelshelf.Games;

namespace Reelshelf.Core.Models;

public record Clip(
    Guid ClipId,
    Guid OwnerId,
    Guid VideoId,
    Guid GameCategoryId,
    string CategorySlug,
    DateTimeOffset CreatedAt,
    BunnyVideo Video,
    IReadOnlyList<string> Tags,
    bool IsViewed,
    ClipShareSummary Share,
    object? GameMetadata)
{
}

public record PagedClipsResponse(List<Clip> Clips, long TotalClips, long TotalPages);

/// <summary>
/// Whole-library totals, computed in the database over every clip the owner has. Clips themselves are
/// paged through the clips endpoint, so nothing here is derived from a subset the client happens to hold.
/// </summary>
public record ClipLibraryTotals(long ClipCount, long UnviewedCount, long DurationSeconds, long StorageBytes);

public record ClipCategoryTotals(
    Guid GameCategoryId,
    long ClipCount,
    long UnviewedCount,
    long DurationSeconds,
    long StorageBytes);

public record ClipLibraryResponse(
    List<GameCategoryResponse> Categories,
    ClipLibraryTotals Totals,
    List<ClipCategoryTotals> CategoryTotals);

public record ClipShareSummary(bool Shared);

public record ClipShareResponse(string SharePath, bool Shared);

public record SharedClipResponse(
    string Title,
    string Game,
    int DurationSeconds,
    DateTimeOffset UploadedAt,
    string EmbedUrl);

public record ApexClipMetadata(
    string? DetectedLegend,
    string? DetectedLegendCard
);
