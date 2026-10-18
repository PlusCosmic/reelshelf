using Reelshelf.Core;
using Reelshelf.Core.Models;
using Reelshelf.Video;

namespace Reelshelf.Bunny;

/// <summary>Media URLs for a video on Bunny Stream, served from its pull zone (<c>BunnyCdnBaseUrl</c>).</summary>
public class BunnyMediaUrls(IConfiguration configuration) : IClipVideoMediaUrls
{
    /// <summary>A Bunny video is its HLS playlist alone.</summary>
    public List<PlaybackSource> Sources(Guid videoId)
    {
        return [new PlaybackSource(Playlist(videoId).ToString(), ClipMediaUrls.HlsType)];
    }

    public Uri Playlist(Guid videoId)
    {
        return new Uri($"{VideoBaseUrl(videoId)}/playlist.m3u8");
    }

    public string Thumbnail(Guid videoId)
    {
        return $"{VideoBaseUrl(videoId)}/thumbnail.jpg";
    }

    /// <summary>
    /// The thumbnails Bunny generates while encoding: the main thumbnail plus five more taken through the video.
    /// </summary>
    public List<string> DetectionFrames(Guid videoId)
    {
        string baseUrl = VideoBaseUrl(videoId);
        return
        [
            $"{baseUrl}/thumbnail.jpg",
            .. Enumerable.Range(1, 5).Select(index => $"{baseUrl}/thumbnail_{index}.jpg")
        ];
    }

    private string VideoBaseUrl(Guid videoId)
    {
        string cdnBaseUrl = configuration["BunnyCdnBaseUrl"]
                            ?? throw new InvalidOperationException("BunnyCdnBaseUrl is not configured");
        return $"{cdnBaseUrl.TrimEnd('/')}/{videoId}";
    }
}
