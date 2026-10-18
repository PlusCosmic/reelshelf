using Reelshelf.Games;
using Reelshelf.Video;

namespace Reelshelf.Core.Models;

public record Clip(
    Guid ClipId,
    Guid OwnerId,
    Guid VideoId,
    Guid GameCategoryId,
    string CategorySlug,
    DateTimeOffset CreatedAt,
    ClipVideo Video,
    IReadOnlyList<string> Tags,
    bool IsViewed,
    ClipShareSummary Share,
    object? GameMetadata)
{
}

/// <summary>
/// A clip's video in the app's own terms, with every media URL built by the API (<see cref="ClipMediaUrls"/>), so
/// clients never name the video host.
/// </summary>
/// <param name="Status">Where the video is on its way to playable.</param>
/// <param name="EncodeProgress">The host's encoding progress, 0 to 100, while <paramref name="Status"/> is processing.</param>
/// <param name="Sources">What players play, in the order they try them (<see cref="PlaybackSource"/>).</param>
public record ClipVideo(
    Guid Id,
    string Title,
    int DurationSeconds,
    ClipVideoStatus Status,
    int EncodeProgress,
    long StorageBytes,
    DateTimeOffset UploadedAt,
    string ThumbnailUrl,
    List<PlaybackSource> Sources);

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
    string ThumbnailUrl,
    List<PlaybackSource> Sources);
