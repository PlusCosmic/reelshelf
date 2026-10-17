namespace Reelshelf.Core;

/// <summary>
/// Every media URL for a clip's video: its HLS playlist and thumbnails. Nothing else builds these, so the video
/// host is only named here and in configuration (<c>BunnyCdnBaseUrl</c>).
/// </summary>
public class ClipMediaUrls(IConfiguration configuration)
{
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
