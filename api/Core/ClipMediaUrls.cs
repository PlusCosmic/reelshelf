using Reelshelf.Video;

namespace Reelshelf.Core;

/// <summary>
/// Every media URL for a clip's video: its HLS playlist and thumbnails. Nothing else builds these. Each takes the
/// clip's <c>video_host</c> and asks that host's <see cref="IClipVideoMediaUrls"/>, so the hosts are only named in
/// their own folders and in configuration.
/// </summary>
public class ClipMediaUrls(IServiceProvider services)
{
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
