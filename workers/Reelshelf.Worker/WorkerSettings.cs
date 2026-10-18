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

    /// <summary>SVT-AV1 preset, 0 (slowest, best) to 13 (fastest).</summary>
    public int Preset { get; set; } = 8;

    /// <summary>SVT-AV1 <c>lp</c> (level of parallelism, roughly threads); 0 leaves it to SVT-AV1, which uses every core.</summary>
    public int LogicalProcessors { get; set; }

    /// <summary>The AV1 output's target video bitrate (VBR).</summary>
    public int TargetBitrateKbps { get; set; } = 4000;

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
    /// On SIGTERM a job whose encode has started gets this long to finish before it is stopped and released back to
    /// the queue (which refunds its attempt); one that hasn't started encoding is released at once. The container's
    /// stop timeout must be longer.
    /// </summary>
    public TimeSpan ShutdownGrace { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>The longest gap between progress reports while a job runs, which also serve as its heartbeat.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);

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

        if (LogicalProcessors < 0 || TargetBitrateKbps <= 0 || AudioBitrateKbps <= 0 || MaxSourceBytes <= 0)
        {
            return "Worker:LogicalProcessors, TargetBitrateKbps, AudioBitrateKbps and MaxSourceBytes must be positive";
        }

        if (Niceness is < 0 or > 19)
        {
            return "Worker:Niceness must be between 0 and 19";
        }

        return null;
    }
}
