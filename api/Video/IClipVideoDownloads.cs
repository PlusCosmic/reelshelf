namespace Reelshelf.Video;

/// <summary>
/// A host whose video is a file the browser can fetch straight from storage, so a download is a redirect rather than
/// work on the API server. Registered as a keyed service under the host's name, beside its <see cref="IClipVideoHost"/>;
/// a host without one (Bunny) is downloaded by remuxing its stream (<c>api/FFmpeg/</c>).
/// </summary>
public interface IClipVideoDownloads
{
    /// <summary>
    /// A short-lived URL that downloads <paramref name="video"/>'s current file as an attachment named
    /// <paramref name="fileName"/>. Only meaningful for a <c>ready</c> clip; the caller checks.
    /// </summary>
    string DownloadUrl(ClipVideoRef video, string fileName);
}
