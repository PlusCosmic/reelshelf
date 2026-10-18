using Reelshelf.Core.Models;
using Reelshelf.Video;

namespace Reelshelf.Core;

/// <summary>
/// Every media URL for a clip's video: its playback sources, server-side stream and thumbnails. Nothing else builds
/// these. Each takes the clip's <see cref="ClipVideoRef"/> and asks its host's <see cref="IClipVideoMediaUrls"/>, so the
/// hosts are only named in their own folders and in configuration.
/// </summary>
public class ClipMediaUrls(IServiceProvider services)
{
    public const string HlsType = "application/vnd.apple.mpegurl";

    /// <summary>
    /// What players play, in the order they try them. None until the video is ready, at every host, so no player
    /// tries a file that doesn't exist yet; clients show the clip's processing state instead.
    /// </summary>
    public List<PlaybackSource> Sources(ClipVideoRef video)
    {
        IClipVideoMediaUrls host = For(video.VideoHost);
        return video.Status.IsPlayable() ? host.Sources(video) : [];
    }

    /// <summary>The stream for server-side readers (ffmpeg); players use <see cref="Sources"/>.</summary>
    public Uri ServerStream(ClipVideoRef video)
    {
        return For(video.VideoHost).ServerStream(video);
    }

    /// <summary>The main thumbnail, or null while the host has none to show yet (see <see cref="IClipVideoMediaUrls.Thumbnail"/>).</summary>
    public string? Thumbnail(ClipVideoRef video)
    {
        return For(video.VideoHost).Thumbnail(video);
    }

    /// <summary>
    /// The six frames legend detection reads: the main thumbnail plus five more taken through the video. Empty while
    /// the host has none yet, like <see cref="Thumbnail"/>.
    /// </summary>
    public List<string> DetectionFrames(ClipVideoRef video)
    {
        return For(video.VideoHost).DetectionFrames(video);
    }

    /// <summary>The video's folder relative to its host's media origin, for the Discord Activity's URL Mappings.</summary>
    public string MediaPath(ClipVideoRef video)
    {
        return For(video.VideoHost).MediaPath(video);
    }

    private IClipVideoMediaUrls For(string videoHost)
    {
        return services.GetKeyedService<IClipVideoMediaUrls>(videoHost)
               ?? throw new InvalidOperationException($"No video host is registered as '{videoHost}'");
    }
}
