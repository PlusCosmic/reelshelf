namespace Reelshelf.Video;

/// <summary>
/// What a clip's media URLs are built from: the host that holds its video and the video's id there, plus what our own
/// pipeline records per clip (V42): the current <paramref name="VideoVersion"/> and that version's
/// <paramref name="Codecs"/> and <paramref name="Height"/>. Bunny needs only the video id; an R2 clip's video id is
/// its clip id.
/// </summary>
/// <param name="Codecs">The MP4's RFC 6381 codecs string (e.g. <c>av01.0.08M.08,opus</c>), when known.</param>
/// <param name="Height">The MP4's height in pixels, when known.</param>
/// <param name="Status">
/// Where the video is on its way to playable. Players get no sources until it is ready, so nothing asks for a file
/// that doesn't exist yet. A ref made by hand describes a ready video.
/// </param>
/// <param name="ThumbnailsReady">
/// True once the thumbnail and detection frames of <paramref name="VideoVersion"/> exist: the clip is ready, or our
/// own worker reported them (<c>transcode_run.thumbnails_ready_at</c>) before encoding. Only our own pipeline reads
/// it; Bunny makes its thumbnails itself.
/// </param>
public sealed record ClipVideoRef(
    string VideoHost,
    Guid VideoId,
    int VideoVersion = 1,
    string? Codecs = null,
    int? Height = null,
    ClipVideoStatus Status = ClipVideoStatus.Ready,
    bool ThumbnailsReady = false);

/// <summary>
/// A row read with the clip columns a <see cref="ClipVideoRef"/> is made from: <c>video_host</c>, <c>video_id</c>,
/// <c>video_version</c>, <c>video_codecs</c>, <c>video_height</c> and <c>video_state</c>, plus
/// <see cref="ClipVideoColumns.ThumbnailsReadySql"/>. A query that shows a clip's media selects all seven.
/// </summary>
public interface IClipVideoColumns
{
    string VideoHost { get; }
    Guid VideoId { get; }
    int VideoVersion { get; }
    string? VideoCodecs { get; }
    int? VideoHeight { get; }

    /// <summary>
    /// The clip's <c>video_state</c>. Rows default to <c>awaiting_upload</c>, so a query that leaves the column out
    /// hands out no sources rather than a file that may not exist.
    /// </summary>
    ClipVideoStatus VideoStatus { get; }

    /// <summary><see cref="ClipVideoColumns.ThumbnailsReadySql"/>; false when a query leaves it out.</summary>
    bool ThumbnailsReady { get; }
}

public static class ClipVideoColumns
{
    public static ClipVideoRef ToVideoRef(this IClipVideoColumns row) =>
        new(row.VideoHost, row.VideoId, row.VideoVersion, row.VideoCodecs, row.VideoHeight, row.VideoStatus,
            row.ThumbnailsReady);

    /// <summary>
    /// The <c>thumbnails_ready</c> column for a query over <c>clip</c> aliased <paramref name="clip"/>: the clip is
    /// ready, or a worker has reported the thumbnails of its current version (a transcode uploads them before it
    /// encodes). A trim's new version reports its own, but the clip stays on the old, ready version until the trim
    /// succeeds. Reads <c>transcode_run_clip_idx</c>.
    /// </summary>
    public static string ThumbnailsReadySql(string clip) =>
        $"""
        ({clip}.video_state = 'ready' OR EXISTS (
            SELECT 1 FROM transcode_run thumbnails_run
            WHERE thumbnails_run.clip_id = {clip}.id
              AND thumbnails_run.video_version = {clip}.video_version
              AND thumbnails_run.thumbnails_ready_at IS NOT NULL)) AS thumbnails_ready
        """;
}
