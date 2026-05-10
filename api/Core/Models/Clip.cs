using Reelshelf.Bunny.Models;

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
