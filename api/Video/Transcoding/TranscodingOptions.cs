using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Reelshelf.Video.Transcoding;

public sealed class TranscodingOptions
{
    public const string SectionName = "Transcoding";

    /// <summary>
    /// Attempts a job gets, counting every claim (a stale reclaim included). A <c>transcode</c> that runs out sets its
    /// clip to <c>encode_failed</c>.
    /// </summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// How many times a job can be handed back by a shutting-down worker (<c>POST /api/worker/{runId}/release</c>)
    /// with its attempt refunded. Past this, a release still requeues the job but keeps the attempt, so a worker
    /// that is restarted for ever runs the job out of <see cref="MaxAttempts"/> instead of bouncing it without end.
    /// </summary>
    public int MaxReleases { get; set; } = 10;

    /// <summary>How long a job that failed a retryable attempt waits before it is claimed again.</summary>
    public TimeSpan RetryAfter { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// A running job whose worker has neither claimed nor reported progress for this long is reclaimed by the next
    /// claim, so a worker that died without a word (a preempted Spot VM, a crash) holds its job only this long. Workers
    /// send progress at least every 30 s for the whole job, transfers included, so this is ten of those.
    /// </summary>
    public TimeSpan StaleAfter { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long a claimed job's presigned URLs live. Longer than the worst-case job, download, encode and uploads
    /// included, since a worker can't renew them; at most 7 days (R2's limit).
    /// </summary>
    public TimeSpan PresignLifetime { get; set; } = TimeSpan.FromHours(2);

    /// <summary>
    /// Queue legend detection when a transcode reports its thumbnails ready, instead of waiting for the encode to
    /// finish. Detection needs only the frames.
    /// </summary>
    public bool QueueDetectionOnThumbnails { get; set; }

    /// <summary>
    /// Queue an <c>audio</c> job for every ready clip whose output carries Opus audio (<see cref="AacBackfillService"/>),
    /// so it plays in Safari. Off by default: turn it on once every worker runs a build that knows the job type.
    /// </summary>
    public bool AacBackfill { get; set; }

    /// <summary>How many backfill <c>audio</c> jobs may wait or run at once; each pass tops the queue up to this.</summary>
    public int AacBackfillBatchSize { get; set; } = 10;

    /// <summary>How often the backfill tops the queue up.</summary>
    public TimeSpan AacBackfillInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The job types a pool of workers may claim, by the name of its worker secret
    /// (<c>Transcoding:WorkerJobTypes:aws=transcode,trim,thumbnails</c>; <see cref="WorkerOptions.DefaultSecretName"/>
    /// for <c>Worker:Secret</c>), comma-separated. A pool with no entry, or an empty one, claims every type. It keeps
    /// jobs off workers where they cost more than elsewhere: an overflow worker pays for every byte it uploads, and a
    /// backfill <c>audio</c> job uploads a whole video for a few seconds of work, so those wait for the dedicated
    /// workers. Keep <c>Overflow:JobTypes</c> to the same types, or the scaler starts workers for jobs they can't take.
    /// </summary>
    public Dictionary<string, string?> WorkerJobTypes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public int EffectiveMaxAttempts => Math.Max(1, MaxAttempts);

    public int EffectiveMaxReleases => Math.Max(0, MaxReleases);

    /// <summary>The job types the named pool's workers may claim; empty means every type.</summary>
    public IReadOnlyList<string> JobTypesFor(string? worker) =>
        SplitJobTypes(WorkerJobTypes
            .FirstOrDefault(entry => string.Equals(entry.Key.Trim(), worker, StringComparison.OrdinalIgnoreCase))
            .Value);

    /// <summary>What is wrong with <see cref="WorkerJobTypes"/>, for startup: a job type that doesn't exist.</summary>
    public IReadOnlyList<string> WorkerJobTypeProblems() =>
    [
        .. WorkerJobTypes
            .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Select(entry => (entry.Key, Unknown: SplitJobTypes(entry.Value).Except(TranscodeRunTypes.All).ToList()))
            .Where(entry => entry.Unknown.Count > 0)
            .Select(entry =>
                $"{SectionName}:{nameof(WorkerJobTypes)}:{entry.Key} has unknown job types: {string.Join(", ", entry.Unknown)}")
    ];

    private static List<string> SplitJobTypes(string? types) =>
        (types ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(type => type.ToLowerInvariant())
        .Distinct()
        .ToList();
}

/// <summary>Fails startup on a <see cref="TranscodingOptions.WorkerJobTypes"/> entry naming a job type that doesn't exist.</summary>
public sealed class TranscodingOptionsValidation : IValidateOptions<TranscodingOptions>
{
    public ValidateOptionsResult Validate(string? name, TranscodingOptions options)
    {
        IReadOnlyList<string> problems = options.WorkerJobTypeProblems();
        return problems.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(problems);
    }
}

/// <summary>The job types in <c>transcode_run.type</c>; see reelshelf-docs/video-pipeline/workers-and-jobs.md.</summary>
public static class TranscodeRunTypes
{
    /// <summary>The uploaded source into a new AV1 video version, with its thumbnail and detection frames.</summary>
    public const string Transcode = "transcode";

    /// <summary>Regenerated thumbnail and detection frames from an existing output, without re-encoding.</summary>
    public const string Thumbnails = "thumbnails";

    /// <summary>A shorter video version cut from the current output.</summary>
    public const string Trim = "trim";

    /// <summary>
    /// A new video version from the current output with its video copied and its audio re-encoded as AAC-LC, for
    /// outputs encoded with Opus, which Safari doesn't play in MP4 (<see cref="AacBackfillService"/> queues them).
    /// </summary>
    public const string Audio = "audio";

    /// <summary>The on-demand H.264 file.</summary>
    public const string Fallback = "fallback";

    public static readonly IReadOnlyList<string> All = [Transcode, Thumbnails, Trim, Audio, Fallback];

    /// <summary>The types that write a new video version from the current output and move the clip to it once done.</summary>
    public static bool MakesVersionFromOutput(string type) => type is Trim or Audio;

    /// <summary>The types whose completion moves the clip to the job's video version.</summary>
    public static bool MovesClip(string type) => type is Transcode || MakesVersionFromOutput(type);

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
/// <param name="FrameAccurate">
/// For a <c>trim</c>: re-encode the range instead of stream-copying it from the nearest keyframes. The worker reads it
/// only when true.
/// </param>
public sealed record TranscodeRunInput(
    string? SourceKey = null,
    double? TrimStartSeconds = null,
    double? TrimEndSeconds = null,
    bool? FrameAccurate = null)
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}
