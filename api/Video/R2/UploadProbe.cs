using FFMpegCore;
using FFMpegCore.Exceptions;

namespace Reelshelf.Video.R2;

/// <summary>What a cheap ffprobe of an uploaded source found.</summary>
public sealed record UploadProbeResult(bool HasVideo, double DurationSeconds, int Width, int Height);

/// <summary>ffprobe could read the upload but not as a video, or not at all.</summary>
public sealed class UnreadableUploadException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Probes an uploaded source over a presigned GET before its transcode is queued. Throws
/// <see cref="UnreadableUploadException"/> for a file ffprobe can't read, and <see cref="TimeoutException"/> when it
/// couldn't finish (a slow or failing read), which says nothing about the file.
/// </summary>
public interface IUploadProbe
{
    Task<UploadProbeResult> ProbeAsync(Uri source, CancellationToken cancellationToken);
}

/// <summary>
/// ffprobe through FFMpegCore, reading the headers over HTTP range requests rather than the whole file, bounded by
/// <see cref="Timeout"/>. The worker probes the whole source again before encoding, and handles HDR (tone-mapped)
/// and variable frame rate (forced to 30 fps CFR) itself, so this looks only for files that are clearly unusable.
/// </summary>
public sealed class FfprobeUploadProbe : IUploadProbe
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public async Task<UploadProbeResult> ProbeAsync(Uri source, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        IMediaAnalysis analysis;
        try
        {
            analysis = await FFProbe.AnalyseAsync(source, cancellationToken: timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"ffprobe took longer than {Timeout.TotalSeconds:0} s");
        }
        catch (Exception error) when (error is FFMpegException or FormatException or InvalidOperationException)
        {
            if (IsReadFailure(error.Message))
            {
                throw new TimeoutException("ffprobe could not read the upload from storage", error);
            }

            throw new UnreadableUploadException("ffprobe could not read the file", error);
        }

        VideoStream? video = analysis.PrimaryVideoStream;
        return new UploadProbeResult(
            video is not null,
            analysis.Duration.TotalSeconds,
            video?.Width ?? 0,
            video?.Height ?? 0);
    }

    /// <summary>ffprobe's errors for an HTTP read that failed, as opposed to a file it couldn't make sense of.</summary>
    internal static bool IsReadFailure(string message) =>
        message.Contains("Server returned", StringComparison.Ordinal)
        || message.Contains("HTTP error", StringComparison.Ordinal)
        || message.Contains("Connection refused", StringComparison.Ordinal)
        || message.Contains("Connection reset", StringComparison.Ordinal)
        || message.Contains("timed out", StringComparison.OrdinalIgnoreCase);
}

/// <summary>The checks an uploaded source must pass before its transcode is queued.</summary>
public static class UploadValidation
{
    /// <summary>Why the uploaded object can't be the clip's file, or null when its size is the one declared.</summary>
    public static string? SizeProblem(long declaredSize, long uploadedSize) =>
        uploadedSize == declaredSize
            ? null
            : $"The uploaded file is {uploadedSize} bytes, but the upload was prepared for {declaredSize}.";

    /// <summary>Why the file is clearly not a usable video, or null when it may be.</summary>
    public static string? Problem(UploadProbeResult probe)
    {
        if (!probe.HasVideo)
        {
            return "The file has no video in it.";
        }

        if (probe.Width <= 0 || probe.Height <= 0)
        {
            return "The video has no picture size.";
        }

        if (!(probe.DurationSeconds > 0))
        {
            return "The video has no length.";
        }

        return null;
    }
}
