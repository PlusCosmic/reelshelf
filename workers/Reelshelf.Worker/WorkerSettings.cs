namespace Reelshelf.Worker;

/// <summary>
/// The worker's configuration, the <c>Worker</c> section (environment variables such as <c>Worker__ApiBaseUrl</c>).
/// The worker holds only the API's address and secret: storage access comes as presigned URLs on each job.
/// </summary>
public sealed class WorkerSettings
{
    public const string SectionName = "Worker";

    /// <summary>The API's secret is refused when shorter than this, so the worker refuses it too.</summary>
    public const int MinimumSecretLength = 32;

    /// <summary><see cref="PreemptionNotice"/>: watch GCE's metadata server.</summary>
    public const string GcePreemptionNotice = "gce";

    /// <summary>The site the API is served from, e.g. <c>https://reelshelf.example</c>; requests go to <c>/api/worker/*</c>.</summary>
    public string ApiBaseUrl { get; set; } = "";

    /// <summary>The API's <c>Worker:Secret</c>, sent as <c>Authorization: Bearer</c> to the API only.</summary>
    public string Secret { get; set; } = "";

    /// <summary>How long to wait after a claim finds nothing; doubles while the queue stays empty.</summary>
    public TimeSpan IdlePollInterval { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan MaxIdlePollInterval { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>How long to wait after the API cannot be reached or errors; doubles while it keeps failing.</summary>
    public TimeSpan ErrorBackoff { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan MaxErrorBackoff { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// When set, the worker exits (code 0) once it has gone this long without claiming a job, for one-shot overflow
    /// workers that should stop once the queue is empty. Time spent backing off after API errors doesn't count. Unset
    /// (the default) polls for ever.
    /// </summary>
    public TimeSpan? ExitWhenIdleAfter { get; set; }

    /// <summary>SVT-AV1 preset, 0 (slowest, best) to 13 (fastest).</summary>
    public int Preset { get; set; } = 8;

    /// <summary>SVT-AV1 <c>lp</c> (level of parallelism, roughly threads); 0 leaves it to SVT-AV1, which uses every core.</summary>
    public int LogicalProcessors { get; set; }

    /// <summary>The AV1 output's target video bitrate (VBR).</summary>
    public int TargetBitrateKbps { get; set; } = 4000;

    /// <summary>The target video bitrate (VBR) instead of <see cref="TargetBitrateKbps"/> when the output is above 32 fps.</summary>
    public int HighFrameRateBitrateKbps { get; set; } = 6000;

    /// <summary>The Opus audio bitrate.</summary>
    public int AudioBitrateKbps { get; set; } = 128;

    /// <summary>ffmpeg and ffprobe run under <c>nice -n</c> this value; 0 runs them at the worker's own priority.</summary>
    public int Niceness { get; set; } = 10;

    public string FfmpegPath { get; set; } = "ffmpeg";

    public string FfprobePath { get; set; } = "ffprobe";

    /// <summary>Each job gets its own directory under this one, deleted when the job ends.</summary>
    public string WorkDirectory { get; set; } = Path.Combine(Path.GetTempPath(), "reelshelf-worker");

    /// <summary>A source larger than this is refused as bad input, so one upload can't fill the disk.</summary>
    public long MaxSourceBytes { get; set; } = 8L * 1024 * 1024 * 1024;

    /// <summary>
    /// How long a <c>migrate</c> job's remux of its HLS source may take, retries included. It is network-bound
    /// (a copy, no encode); a remux that runs out of time fails as retryable.
    /// </summary>
    public TimeSpan RemuxTimeout { get; set; } = TimeSpan.FromMinutes(20);

    /// <summary>
    /// On SIGTERM a job whose encode has started gets this long to finish before it is stopped and released back to
    /// the queue (which refunds its attempt); one that hasn't started encoding is released at once. The container's
    /// stop timeout must be longer.
    /// </summary>
    public TimeSpan ShutdownGrace { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>The longest gap between progress reports while a job runs, which also serve as its heartbeat.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Where the worker learns its Spot VM is being preempted, so it hands its job back at once instead of counting
    /// on SIGTERM: empty (the default) for none, or <c>gce</c> for GCE's metadata server, which the Batch overflow
    /// pool sets. See <see cref="IPreemptionNotice"/>.
    /// </summary>
    public string PreemptionNotice { get; set; } = "";

    /// <summary>GCE's metadata server, for <see cref="PreemptionNotice"/> <c>gce</c>; tests point it elsewhere.</summary>
    public string GceMetadataUrl { get; set; } = "http://169.254.169.254/computeMetadata/v1";

    public bool WatchesGcePreemption =>
        string.Equals(PreemptionNotice?.Trim(), GcePreemptionNotice, StringComparison.OrdinalIgnoreCase);

    /// <summary>Why the settings can't be used, or null when they can.</summary>
    public string? Problem()
    {
        if (!Uri.TryCreate(ApiBaseUrl, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https"))
        {
            return "Worker:ApiBaseUrl must be an absolute http(s) URL";
        }

        if (Secret.Trim().Length < MinimumSecretLength)
        {
            return $"Worker:Secret must be at least {MinimumSecretLength} characters, as the API requires";
        }

        if (Preset is < 0 or > 13)
        {
            return "Worker:Preset must be between 0 and 13";
        }

        if (LogicalProcessors < 0 || TargetBitrateKbps <= 0 || HighFrameRateBitrateKbps <= 0 || AudioBitrateKbps <= 0 ||
            MaxSourceBytes <= 0)
        {
            return "Worker:LogicalProcessors, TargetBitrateKbps, HighFrameRateBitrateKbps, AudioBitrateKbps and MaxSourceBytes must be positive";
        }

        if (ExitWhenIdleAfter <= TimeSpan.Zero)
        {
            return "Worker:ExitWhenIdleAfter must be positive when set";
        }

        if (RemuxTimeout <= TimeSpan.Zero)
        {
            return "Worker:RemuxTimeout must be positive";
        }

        if (Niceness is < 0 or > 19)
        {
            return "Worker:Niceness must be between 0 and 19";
        }

        if (!string.IsNullOrWhiteSpace(PreemptionNotice) && !WatchesGcePreemption)
        {
            return $"Worker:PreemptionNotice must be empty or {GcePreemptionNotice}";
        }

        return null;
    }
}
