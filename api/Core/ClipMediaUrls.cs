namespace Reelshelf.Core;

/// <summary>
/// Every media URL for a clip's video: its HLS playlist, thumbnails and embed player. Nothing else builds these,
/// so the video host is only named here and in configuration (<c>BunnyCdnBaseUrl</c>, <c>BunnyLibraryId</c>).
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

    public string Embed(Guid videoId, bool autoplay = false)
    {
        string libraryId = configuration["BunnyLibraryId"]
                           ?? throw new InvalidOperationException("Bunny API library ID not configured");
        return $"https://player.mediadelivery.net/embed/{libraryId}/{videoId}?autoplay={(autoplay ? "true" : "false")}";
    }

    private string VideoBaseUrl(Guid videoId)
    {
        string cdnBaseUrl = configuration["BunnyCdnBaseUrl"]
                            ?? throw new InvalidOperationException("BunnyCdnBaseUrl is not configured");
        return $"{cdnBaseUrl.TrimEnd('/')}/{videoId}";
    }
}
