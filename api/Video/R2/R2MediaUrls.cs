using Reelshelf.Core.Models;

namespace Reelshelf.Video.R2;

/// <summary>
/// Media URLs for an R2 clip, on the media domain (<c>R2:MediaBaseUrl</c>) under its current version's folder
/// (<see cref="R2ObjectKeys"/>). Unsigned for now, like Bunny's. The thumbnails are handed out only once they exist
/// (<see cref="ClipVideoRef.ThumbnailsReady"/>): the worker uploads them before it encodes, and a request made before
/// that would get a 404 the browser never retries and Cloudflare may cache.
/// </summary>
public class R2MediaUrls(R2Options options) : IClipVideoMediaUrls
{
    public const string Mp4Type = "video/mp4";

    /// <summary>
    /// The one AV1 MP4, typed with its codecs so a player can ask the browser whether it decodes them before
    /// trying, and with its height. Without codecs (a worker that didn't report them) it is plain <c>video/mp4</c>.
    /// </summary>
    public List<PlaybackSource> Sources(ClipVideoRef video)
    {
        string type = video.Codecs is null ? Mp4Type : $"{Mp4Type}; codecs=\"{video.Codecs}\"";
        return [new PlaybackSource(File(video, R2ObjectKeys.Av1), type, video.Height)];
    }

    /// <summary>ffmpeg reads the progressive MP4 directly.</summary>
    public Uri ServerStream(ClipVideoRef video)
    {
        return new Uri(File(video, R2ObjectKeys.Av1));
    }

    public string? Thumbnail(ClipVideoRef video)
    {
        return ThumbnailsExist(video) ? File(video, R2ObjectKeys.Thumbnail) : null;
    }

    /// <summary>The worker writes the same six frames Bunny does, under the same names.</summary>
    public List<string> DetectionFrames(ClipVideoRef video)
    {
        return ThumbnailsExist(video)
            ? R2ObjectKeys.Thumbnails.Select(fileName => File(video, fileName)).ToList()
            : [];
    }

    public string MediaPath(ClipVideoRef video)
    {
        return R2ObjectKeys.VersionFolder(video.VideoId, video.VideoVersion);
    }

    private static bool ThumbnailsExist(ClipVideoRef video) => video.Status.IsPlayable() || video.ThumbnailsReady;

    private string File(ClipVideoRef video, string fileName)
    {
        return $"{options.MediaBaseUrl.TrimEnd('/')}/{R2ObjectKeys.Output(video.VideoId, video.VideoVersion, fileName)}";
    }
}
