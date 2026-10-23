using Reelshelf.Video;
using Reelshelf.Video.R2;

namespace Reelshelf.Plans;

/// <summary>Exports in R2, and the clips' videos they are made from.</summary>
public sealed class R2ExportStorage(R2Storage storage) : IExportStorage
{
    public bool IsConfigured => true;

    public Task UploadAsync(string key, Stream content, CancellationToken cancellationToken) =>
        storage.UploadAsync(key, content, "application/zip", cancellationToken);

    public Task DeletePrefixAsync(string prefix) => storage.DeletePrefixAsync(prefix);

    public string DownloadUrl(string key, string fileName) =>
        storage.PresignGet(key, R2Downloads.Lifetime, R2Downloads.AttachmentDisposition(fileName));
}

/// <summary>Without R2 there is nowhere to put exports, so none are made and none can be asked for.</summary>
public sealed class UnconfiguredExportStorage : IExportStorage
{
    public bool IsConfigured => false;

    public Task UploadAsync(string key, Stream content, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Exports need R2");

    public Task DeletePrefixAsync(string prefix) => Task.CompletedTask;

    public string DownloadUrl(string key, string fileName) => throw new InvalidOperationException("Exports need R2");
}

/// <summary>
/// A clip's current video for an export: an R2 clip's <c>av1.mp4</c> read through a presigned GET, or a Bunny clip's
/// HLS stream remuxed into an MP4 on disk by <see cref="FFmpeg.FFmpegService"/>, as its download is. Clips that
/// aren't ready have nothing to export.
/// </summary>
public sealed class ClipExportVideoSource(
    IClipVideoReader r2,
    FFmpeg.FFmpegService ffmpeg,
    IHttpClientFactory httpClientFactory) : IExportVideoSource
{
    public const string HttpClientName = "clip-export";

    public async Task<ExportVideo?> OpenAsync(ExportClipRow clip, CancellationToken cancellationToken)
    {
        if (clip.VideoStatus != ClipVideoStatus.Ready)
        {
            return null;
        }

        ClipVideoRef video = clip.ToVideoRef();
        if (video.VideoHost == VideoHostNames.R2)
        {
            string? url = r2.VideoUrl(video);
            if (url is null)
            {
                return null;
            }

            HttpClient client = httpClientFactory.CreateClient(HttpClientName);
            HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                response.Dispose();
                return null;
            }

            response.EnsureSuccessStatusCode();
            return new ExportVideo(await response.Content.ReadAsStreamAsync(cancellationToken), response.Dispose);
        }

        FFmpeg.FFmpegService.DownloadedVideo downloaded = await ffmpeg.DownloadHlsVideoAsync(video, cancellationToken);
        try
        {
            FileStream file = new(downloaded.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024,
                FileOptions.Asynchronous);
            return new ExportVideo(file, () =>
            {
                downloaded.Dispose();
                ffmpeg.DeleteDownloadedVideo(downloaded.Path);
            });
        }
        catch
        {
            downloaded.Dispose();
            ffmpeg.DeleteDownloadedVideo(downloaded.Path);
            throw;
        }
    }
}

/// <summary>Presigned reads of R2 clips' videos, when R2 is configured.</summary>
public interface IClipVideoReader
{
    string? VideoUrl(ClipVideoRef video);
}

public sealed class R2VideoReader(R2Storage storage) : IClipVideoReader
{
    public string? VideoUrl(ClipVideoRef video) =>
        storage.PresignGet(R2ObjectKeys.Output(video.VideoId, video.VideoVersion, R2ObjectKeys.Av1), TimeSpan.FromHours(1));
}

public sealed class NoVideoReader : IClipVideoReader
{
    public string? VideoUrl(ClipVideoRef video) => null;
}
