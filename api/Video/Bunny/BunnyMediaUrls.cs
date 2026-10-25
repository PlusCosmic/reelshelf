using Reelshelf.Core;
using Reelshelf.Core.Models;

namespace Reelshelf.Video.Bunny;

/// <summary>
/// Media URLs for a video on Bunny Stream, served from its pull zone (<c>BunnyCdnBaseUrl</c>). Bunny keeps one
/// folder per video id; the version, codecs and height our own pipeline records don't apply.
/// </summary>
public class BunnyMediaUrls(IConfiguration configuration) : IClipVideoMediaUrls
{
    /// <summary>A Bunny video is its HLS playlist alone.</summary>
    public List<PlaybackSource> Sources(ClipVideoRef video)
    {
        return [new PlaybackSource(Playlist(video.VideoId).ToString(), ClipMediaUrls.HlsType)];
    }

    /// <summary>The HLS playlist, which ffmpeg reads like any other input.</summary>
    public Uri ServerStream(ClipVideoRef video)
    {
        return Playlist(video.VideoId);
    }

    /// <summary>
    /// Handed out from the start, whatever the status: Bunny makes its thumbnails early in its own processing, and
    /// nothing here says when.
    /// </summary>
    public string? Thumbnail(ClipVideoRef video)
    {
        return $"{VideoBaseUrl(video.VideoId)}/thumbnail.jpg";
    }

    /// <summary>
    /// The thumbnails Bunny generates while encoding: the main thumbnail plus five more taken through the video.
    /// </summary>
    public List<string> DetectionFrames(ClipVideoRef video)
    {
        string baseUrl = VideoBaseUrl(video.VideoId);
        return
        [
            $"{baseUrl}/thumbnail.jpg",
            .. Enumerable.Range(1, 5).Select(index => $"{baseUrl}/thumbnail_{index}.jpg")
        ];
    }

    /// <summary>The pull zone keeps each video's files under its id.</summary>
    public string MediaPath(ClipVideoRef video)
    {
        return video.VideoId.ToString();
    }

    private Uri Playlist(Guid videoId)
    {
        return new Uri($"{VideoBaseUrl(videoId)}/playlist.m3u8");
    }

    private string VideoBaseUrl(Guid videoId)
    {
        string cdnBaseUrl = configuration["BunnyCdnBaseUrl"]
                            ?? throw new InvalidOperationException("BunnyCdnBaseUrl is not configured");
        return $"{cdnBaseUrl.TrimEnd('/')}/{videoId}";
    }
}
