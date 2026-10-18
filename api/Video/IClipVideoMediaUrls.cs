using Reelshelf.Core.Models;

namespace Reelshelf.Video;

/// <summary>
/// One video host's media URLs for a video it holds. Each host registers its own under its name (see
/// <see cref="VideoHostRegistration.AddClipVideoHost{THost, TMediaUrls}"/>); the rest of the API asks
/// <see cref="Core.ClipMediaUrls"/>, which picks the clip's host.
/// </summary>
public interface IClipVideoMediaUrls
{
    /// <summary>
    /// What players play, in the order they try them. Only asked for a ready video; <see cref="Core.ClipMediaUrls"/>
    /// hands out none before that.
    /// </summary>
    List<PlaybackSource> Sources(ClipVideoRef video);

    /// <summary>
    /// The stream server-side readers (ffmpeg) open: Bunny's HLS playlist, or our own pipeline's MP4. Players use
    /// <see cref="Sources"/>.
    /// </summary>
    Uri ServerStream(ClipVideoRef video);

    /// <summary>
    /// The main thumbnail, or null while it doesn't exist yet. A URL handed out before its file exists is requested
    /// at once, and the 404 sticks: browsers don't retry an <c>&lt;img&gt;</c>, and the CDN may cache it.
    /// </summary>
    string? Thumbnail(ClipVideoRef video);

    /// <summary>
    /// The six frames legend detection reads: the main thumbnail plus five more taken through the video. Empty while
    /// they don't exist yet, as for <see cref="Thumbnail"/>.
    /// </summary>
    List<string> DetectionFrames(ClipVideoRef video);

    /// <summary>
    /// The folder holding the video's files, relative to the host's media origin and without slashes at either end.
    /// The Discord Activity reaches each origin through its own URL Mapping, so it builds its paths from this
    /// (<c>frontend/src/shared/utils/clipMediaUrls.ts</c>) rather than taking the URLs above.
    /// </summary>
    string MediaPath(ClipVideoRef video);
}
