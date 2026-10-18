using System.Text.Json;
using System.Text.Json.Serialization;

namespace Reelshelf.Video.Transcoding;

public sealed class TranscodingOptions
{
    public const string SectionName = "Transcoding";

    /// <summary>
    /// Attempts a job gets, counting every claim (a stale reclaim included). A <c>transcode</c> that runs out sets its
    /// clip to <c>encode_failed</c>.
    /// </summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>How long a job that failed a retryable attempt waits before it is claimed again.</summary>
    public TimeSpan RetryAfter { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// A running job whose worker has neither claimed nor reported progress for this long is reclaimed by the next
    /// claim. Longer than the worst-case encode, so a worker that never reports progress still finishes; each
    /// progress report starts it again.
    /// </summary>
    public TimeSpan StaleAfter { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Queue legend detection when a transcode reports its thumbnails ready, instead of waiting for the encode to
    /// finish. Detection needs only the frames.
    /// </summary>
    public bool QueueDetectionOnThumbnails { get; set; }

    public int EffectiveMaxAttempts => Math.Max(1, MaxAttempts);
}

/// <summary>The job types in <c>transcode_run.type</c>; see docs/video-pipeline/workers-and-jobs.md.</summary>
public static class TranscodeRunTypes
{
    /// <summary>The uploaded source into a new AV1 video version, with its thumbnail and detection frames.</summary>
    public const string Transcode = "transcode";

    /// <summary>Regenerated thumbnail and detection frames from an existing output, without re-encoding.</summary>
    public const string Thumbnails = "thumbnails";

    /// <summary>A shorter video version cut from the current output.</summary>
    public const string Trim = "trim";

    /// <summary>The on-demand H.264 file.</summary>
    public const string Fallback = "fallback";

    public static readonly IReadOnlyList<string> All = [Transcode, Thumbnails, Trim, Fallback];

    /// <summary>
    /// Default claim order: someone is waiting on a new upload, a trim or a fallback, while regenerating thumbnails
    /// is backfill work.
    /// </summary>
    public static int DefaultPriority(string type) => type switch
    {
        Transcode => 0,
        Trim or Fallback => 1,
        _ => 2
    };
}

/// <summary>What a job's worker reads, stored as <c>transcode_run.input</c>.</summary>
/// <param name="SourceKey">The storage key the job reads from: the upload for a transcode, else the current output.</param>
/// <param name="TrimStartSeconds">Where a trim starts, when the job applies one.</param>
/// <param name="TrimEndSeconds">Where a trim ends, when the job applies one.</param>
public sealed record TranscodeRunInput(
    string? SourceKey = null,
    double? TrimStartSeconds = null,
    double? TrimEndSeconds = null)
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}
