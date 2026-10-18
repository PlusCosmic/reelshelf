using Reelshelf.Core.Models;

namespace Reelshelf.Video;

/// <summary>
/// One video host's media URLs for a video it holds. Each host registers its own under its name (see
/// <see cref="VideoHostRegistration.AddClipVideoHost{THost, TMediaUrls}"/>); the rest of the API asks
/// <see cref="Core.ClipMediaUrls"/>, which picks the clip's host.
/// </summary>
public interface IClipVideoMediaUrls
{
    /// <summary>What players play, in the order they try them.</summary>
    List<PlaybackSource> Sources(Guid videoId);

    /// <summary>The HLS playlist, for server-side readers (ffmpeg).</summary>
    Uri Playlist(Guid videoId);

    /// <summary>The main thumbnail.</summary>
    string Thumbnail(Guid videoId);

    /// <summary>The six frames legend detection reads: the main thumbnail plus five more taken through the video.</summary>
    List<string> DetectionFrames(Guid videoId);
}
