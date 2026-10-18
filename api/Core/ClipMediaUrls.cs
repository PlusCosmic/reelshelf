using Reelshelf.Core.Models;
using Reelshelf.Video;

namespace Reelshelf.Core;

/// <summary>
/// Every media URL for a clip's video: its playback sources, HLS playlist and thumbnails. Nothing else builds these.
/// Each takes the clip's <c>video_host</c> and asks that host's <see cref="IClipVideoMediaUrls"/>, so the hosts are
/// only named in their own folders and in configuration.
/// </summary>
public class ClipMediaUrls(IServiceProvider services)
{
    public const string HlsType = "application/vnd.apple.mpegurl";

    /// <summary>What players play, in the order they try them.</summary>
    public List<PlaybackSource> Sources(string videoHost, Guid videoId)
    {
        return For(videoHost).Sources(videoId);
    }

    /// <summary>The HLS playlist, for server-side readers (ffmpeg); players use <see cref="Sources"/>.</summary>
    public Uri Playlist(string videoHost, Guid videoId)
    {
        return For(videoHost).Playlist(videoId);
    }

    public string Thumbnail(string videoHost, Guid videoId)
    {
        return For(videoHost).Thumbnail(videoId);
    }

    /// <summary>The six frames legend detection reads: the main thumbnail plus five more taken through the video.</summary>
    public List<string> DetectionFrames(string videoHost, Guid videoId)
    {
        return For(videoHost).DetectionFrames(videoId);
    }

    private IClipVideoMediaUrls For(string videoHost)
    {
        return services.GetKeyedService<IClipVideoMediaUrls>(videoHost)
               ?? throw new InvalidOperationException($"No video host is registered as '{videoHost}'");
    }
}
