namespace Reelshelf.Video;

/// <summary>
/// What a clip's media URLs are built from: the host that holds its video and the video's id there, plus what our own
/// pipeline records per clip (V42): the current <paramref name="VideoVersion"/> and that version's
/// <paramref name="Codecs"/> and <paramref name="Height"/>. Bunny needs only the video id; an R2 clip's video id is
/// its clip id.
/// </summary>
/// <param name="Codecs">The MP4's RFC 6381 codecs string (e.g. <c>av01.0.08M.08,opus</c>), when known.</param>
/// <param name="Height">The MP4's height in pixels, when known.</param>
public sealed record ClipVideoRef(
    string VideoHost,
    Guid VideoId,
    int VideoVersion = 1,
    string? Codecs = null,
    int? Height = null);

/// <summary>
/// A row read with the clip columns a <see cref="ClipVideoRef"/> is made from: <c>video_host</c>, <c>video_id</c>,
/// <c>video_version</c>, <c>video_codecs</c> and <c>video_height</c>. A query that shows a clip's media selects all
/// five.
/// </summary>
public interface IClipVideoColumns
{
    string VideoHost { get; }
    Guid VideoId { get; }
    int VideoVersion { get; }
    string? VideoCodecs { get; }
    int? VideoHeight { get; }
}

public static class ClipVideoColumns
{
    public static ClipVideoRef ToVideoRef(this IClipVideoColumns row) =>
        new(row.VideoHost, row.VideoId, row.VideoVersion, row.VideoCodecs, row.VideoHeight);
}
