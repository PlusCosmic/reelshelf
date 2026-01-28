using Nucleus.Clips.Bunny.Models;

namespace Nucleus.Clips.Core.Models;

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
    object? GameMetadata)
{
}

public record PagedClipsResponse(List<Clip> Clips, long TotalClips, long TotalPages);

public record ApexClipMetadata(
    string? DetectedLegend,
    string? DetectedLegendCard
);
