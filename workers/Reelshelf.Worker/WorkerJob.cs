using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Reelshelf.Worker;

/// <summary>
/// A claimed job, as <c>POST /api/worker/claim</c> returns it (the API's <c>WorkerJobResponse</c>).
/// <see cref="SourceUrl"/> is a presigned GET for the input (for <c>migrate</c>, a public HLS playlist URL) and <see cref="UploadUrls"/> a presigned PUT per output
/// file name; see docs/video-pipeline/workers-and-jobs.md.
/// </summary>
public sealed record WorkerJob(
    Guid RunId,
    Guid LeaseId,
    Guid ClipId,
    string Type,
    int VideoVersion,
    int Attempt,
    JsonElement Input,
    string? SourceUrl,
    IReadOnlyDictionary<string, string>? UploadUrls);

/// <summary>The job types the queue hands out (the API's <c>TranscodeRunTypes</c>).</summary>
public static class JobTypes
{
    public const string Transcode = "transcode";
    public const string Thumbnails = "thumbnails";
    public const string Trim = "trim";
    public const string Fallback = "fallback";

    /// <summary>A clip moving off Bunny: its source is Bunny's HLS master playlist, then it is a <see cref="Transcode"/>.</summary>
    public const string Migrate = "migrate";
}

/// <summary>The output file names, which are the keys of <see cref="WorkerJob.UploadUrls"/>.</summary>
public static class OutputFiles
{
    public const string Video = "av1.mp4";
    public const string Thumbnail = "thumbnail.jpg";
    public const string Preview = "preview.mp4";

    /// <summary><c>thumbnail.jpg</c> then <c>thumbnail_1.jpg</c> … <c>thumbnail_5.jpg</c>, the frames legend detection reads.</summary>
    public static readonly IReadOnlyList<string> Frames =
        [Thumbnail, .. Enumerable.Range(1, FrameTimings.ExtraFrames).Select(index => $"thumbnail_{index}.jpg")];

    public static string ContentType(string fileName) =>
        fileName.EndsWith(".jpg", StringComparison.Ordinal) ? "image/jpeg" : "video/mp4";
}

/// <summary>
/// What a job's <c>input</c> asks for. Trim points are seconds into the job's source; unknown keys are ignored.
/// </summary>
public sealed record JobInput(double? TrimStartSeconds, double? TrimEndSeconds, bool FrameAccurate)
{
    public bool HasTrim => TrimStartSeconds is not null || TrimEndSeconds is not null;

    public static JobInput Parse(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object)
        {
            return new JobInput(null, null, false);
        }

        return new JobInput(
            Number(input, "trim_start_seconds"),
            Number(input, "trim_end_seconds"),
            input.TryGetProperty("frame_accurate", out JsonElement accurate) && accurate.ValueKind == JsonValueKind.True);
    }

    /// <summary>
    /// The part of a source of <paramref name="durationSeconds"/> the job keeps: the trim clamped to the source,
    /// or all of it. Throws <see cref="JobFailedException"/> (not retryable) for a trim that keeps nothing.
    /// </summary>
    public TrimRange Resolve(double durationSeconds)
    {
        double start = Math.Max(0, TrimStartSeconds ?? 0);
        double end = Math.Min(durationSeconds, TrimEndSeconds ?? durationSeconds);
        if (!double.IsFinite(start) || !double.IsFinite(end) || end - start < TrimRange.MinimumSeconds)
        {
            throw new JobFailedException(FormattableString.Invariant(
                $"The trim {TrimStartSeconds}–{TrimEndSeconds}s keeps nothing of a {durationSeconds:0.###}s source"),
                retryable: false);
        }

        return new TrimRange(start, end - start, HasTrim && (start > 0 || end < durationSeconds));
    }

    private static double? Number(JsonElement input, string name) =>
        input.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;
}

/// <summary>The kept part of a source. <see cref="IsCut"/> is false when it is the whole source.</summary>
public sealed record TrimRange(double StartSeconds, double LengthSeconds, bool IsCut)
{
    /// <summary>A trim shorter than this is refused: one frame at 30 fps.</summary>
    public const double MinimumSeconds = 1.0 / 30;

    public static string Seconds(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>A job that could not finish. <see cref="Retryable"/> is false for bad input that will never work.</summary>
public sealed class JobFailedException(string message, bool retryable, Exception? inner = null)
    : Exception(message, inner)
{
    public bool Retryable { get; } = retryable;
}

/// <summary>The API answered 409 (or 404): the job is no longer this worker's, so it is dropped without a report.</summary>
public sealed class LeaseLostException(Guid runId) : Exception($"Job {runId} is no longer running under this worker's lease");

/// <summary>The API could not be reached or answered with an unexpected status.</summary>
public sealed class WorkerApiException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>What a finished job reports on <c>/complete</c>.</summary>
public sealed record JobResult(double LengthSeconds, long SizeBytes, JobOutputs Outputs);

/// <summary>
/// The <c>outputs</c> object of a completion: the RFC 6381 codecs of <c>av1.mp4</c>, its size in pixels, and the files
/// uploaded. The API builds the clip's playback source from <see cref="Codecs"/> and <see cref="Height"/>.
/// </summary>
public sealed record JobOutputs(
    string Codecs,
    int Width,
    int Height,
    IReadOnlyList<string> Files,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? ToneMapped = null);
