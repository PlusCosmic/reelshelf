using System.Text.RegularExpressions;

namespace Reelshelf.Video.Transcoding.Overflow;

/// <summary>
/// The overflow pool (<c>Overflow:*</c>, docs/video-pipeline/workers-and-jobs.md#the-overflow-pool): one-shot transcode
/// workers that the API starts on Google Cloud Batch (<see cref="Batch"/>) when <c>transcode_run</c> backs up. The
/// workers stop themselves once idle, so the API only ever starts them. PlusCosmic/infrastructure's
/// gcp/overflow-workers/setup.sh prints the settings.
/// </summary>
public sealed partial class OverflowOptions
{
    public const string SectionName = "Overflow";

    /// <summary>The longest GCP label value, which <see cref="StartedBy"/> is.</summary>
    public const int MaxGcpLabelLength = 63;

    private static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(5);

    /// <summary>Off by default; on, startup fails unless the Batch settings are all set.</summary>
    public bool Enabled { get; set; }

    /// <summary>How often the queue and the running tasks are counted.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Only these job types (comma-separated, from <see cref="TranscodeRunTypes.All"/>) count towards the backlog;
    /// empty counts every type, <c>migrate</c> included.
    /// </summary>
    public string JobTypes { get; set; } = "";

    /// <summary>
    /// How long the oldest claimable job must have waited before tasks are started, so the dedicated worker gets the
    /// first go at short bursts.
    /// </summary>
    public TimeSpan ScaleUpAfter { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>The most overflow tasks running or provisioning at once: the hard cap on spend.</summary>
    public int MaxTasks { get; set; } = 4;

    /// <summary>The most tasks one tick starts.</summary>
    public int MaxStartPerTick { get; set; } = 4;

    /// <summary>How long after starting tasks no more are started, so tasks still provisioning aren't double-counted.</summary>
    public TimeSpan Cooldown { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>The <c>pool</c> label on every Batch job the API starts, by which it counts them again.</summary>
    public string StartedBy { get; set; } = "reelshelf-overflow";

    public OverflowBatchOptions Batch { get; set; } = new();

    public TimeSpan EffectiveInterval => Interval < MinimumInterval ? MinimumInterval : Interval;

    public IReadOnlyList<string> JobTypeList => SplitList(JobTypes);

    /// <summary>
    /// What is wrong with these settings for an enabled pool, if anything: whatever
    /// <see cref="OverflowBatchOptions.Problems"/> finds, a <see cref="StartedBy"/> that isn't a GCP label value,
    /// unknown job types, or limits below 1.
    /// </summary>
    public IReadOnlyList<string> Problems()
    {
        List<string> problems = [.. Batch.Problems()];
        if (!GcpLabelValue().IsMatch(StartedBy.Trim()))
        {
            problems.Add($"{SectionName}:{nameof(StartedBy)} must be 1 to {MaxGcpLabelLength} lowercase letters, digits, - or _ (it is a label)");
        }

        List<string> unknownTypes = JobTypeList.Where(type => !TranscodeRunTypes.All.Contains(type)).ToList();
        if (unknownTypes.Count > 0)
        {
            problems.Add($"{SectionName}:{nameof(JobTypes)} has unknown job types: {string.Join(", ", unknownTypes)}");
        }

        if (MaxTasks < 1)
        {
            problems.Add($"{SectionName}:{nameof(MaxTasks)} must be at least 1");
        }

        if (MaxStartPerTick < 1)
        {
            problems.Add($"{SectionName}:{nameof(MaxStartPerTick)} must be at least 1");
        }

        if (ScaleUpAfter < TimeSpan.Zero || Cooldown < TimeSpan.Zero)
        {
            problems.Add($"{SectionName}:{nameof(ScaleUpAfter)} and {nameof(Cooldown)} can't be negative");
        }

        return problems;
    }

    private static IReadOnlyList<string> SplitList(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();

    [GeneratedRegex("^[a-z0-9_-]{1,63}$")]
    private static partial Regex GcpLabelValue();
}

/// <summary>
/// The overflow pool on Google Cloud Batch (<c>Overflow:Batch:*</c>, docs/video-pipeline/batch-overflow.md). Every
/// scale-up is one job of that many tasks, one worker VM each, from <see cref="InstanceTemplate"/>, which holds the
/// machine type, Spot, the boot disk, the network and the Standard network tier (Batch gives the VMs it describes
/// itself a Premium-tier IP). PlusCosmic/infrastructure's gcp/overflow-workers/setup.sh creates the resources and
/// prints these settings.
/// </summary>
public sealed class OverflowBatchOptions
{
    private const string Prefix = OverflowOptions.SectionName + ":" + nameof(OverflowOptions.Batch) + ":";

    /// <summary>The GCP project, e.g. <c>reelshelf-512321</c>.</summary>
    public string ProjectId { get; set; } = "";

    /// <summary>Where the jobs live and their VMs run, e.g. <c>europe-west9</c>.</summary>
    public string Region { get; set; } = "";

    /// <summary>
    /// The worker VMs' instance template: a name or <c>projects/{project}/global/instanceTemplates/{name}</c>.
    /// </summary>
    public string InstanceTemplate { get; set; } = "";

    /// <summary>
    /// The template's vCPUs: the task's CPU request and the worker's <c>Worker__LogicalProcessors</c>, so it must match
    /// the machine type.
    /// </summary>
    public int Vcpus { get; set; } = 4;

    /// <summary>The task's memory request; it must fit the machine (a <c>c4-highcpu-4</c> has 8 GB).</summary>
    public int MemoryMib { get; set; } = 6144;

    /// <summary>The worker VMs' service account, which Batch requires to be the template's.</summary>
    public string ServiceAccount { get; set; } = "";

    /// <summary>The worker image, e.g. <c>ghcr.io/pluscosmic/reelshelf-worker:sha-…</c>, kept current by the bump PR.</summary>
    public string WorkerImage { get; set; } = "";

    /// <summary>
    /// The Secret Manager version holding the workers' <c>Worker__Secret</c>, e.g.
    /// <c>projects/{project}/secrets/reelshelf-worker-gcp/versions/latest</c>; the API checks it as
    /// <c>Worker:Secrets:gcp</c>.
    /// </summary>
    public string WorkerSecret { get; set; } = "";

    /// <summary>The site the workers call, e.g. <c>https://reelshelf.app</c>.</summary>
    public string ApiBaseUrl { get; set; } = "";

    /// <summary>The workers' <c>Worker__ExitWhenIdleAfter</c>: how long one waits for a job before it exits.</summary>
    public TimeSpan ExitWhenIdleAfter { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// A cap on one worker's run, for safety only: a long backlog legitimately keeps a worker busy for hours.
    /// </summary>
    public TimeSpan MaxRunDuration { get; set; } = TimeSpan.FromHours(12);

    /// <summary>
    /// A job none of whose tasks has started after this long (no quota, no Spot capacity) is deleted, so it stops
    /// counting against <see cref="OverflowOptions.MaxTasks"/>.
    /// </summary>
    public TimeSpan ProvisionTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The scaler service account's JSON key, base64-encoded. Empty uses Application Default Credentials.
    /// </summary>
    public string CredentialsBase64 { get; set; } = "";

    /// <summary>The settings' problems, for <see cref="OverflowOptions.Problems"/>.</summary>
    public IReadOnlyList<string> Problems()
    {
        List<string> problems = [];
        void Require(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                problems.Add($"{Prefix}{name} is missing");
            }
        }

        Require(ProjectId, nameof(ProjectId));
        Require(Region, nameof(Region));
        Require(InstanceTemplate, nameof(InstanceTemplate));
        Require(ServiceAccount, nameof(ServiceAccount));
        Require(WorkerImage, nameof(WorkerImage));
        Require(WorkerSecret, nameof(WorkerSecret));
        Require(ApiBaseUrl, nameof(ApiBaseUrl));
        if (!string.IsNullOrWhiteSpace(ApiBaseUrl) &&
            (!Uri.TryCreate(ApiBaseUrl.Trim(), UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https")))
        {
            problems.Add($"{Prefix}{nameof(ApiBaseUrl)} must be an absolute http(s) URL");
        }

        if (Vcpus < 1 || MemoryMib < 1)
        {
            problems.Add($"{Prefix}{nameof(Vcpus)} and {nameof(MemoryMib)} must be at least 1");
        }

        if (ExitWhenIdleAfter <= TimeSpan.Zero || MaxRunDuration <= TimeSpan.Zero || ProvisionTimeout <= TimeSpan.Zero)
        {
            problems.Add($"{Prefix}{nameof(ExitWhenIdleAfter)}, {nameof(MaxRunDuration)} and {nameof(ProvisionTimeout)} must be positive");
        }

        if (!string.IsNullOrWhiteSpace(CredentialsBase64) && CredentialsJson() is null)
        {
            problems.Add($"{Prefix}{nameof(CredentialsBase64)} isn't base64");
        }

        return problems;
    }

    /// <summary>The decoded key, or null when <see cref="CredentialsBase64"/> is empty or not base64.</summary>
    public string? CredentialsJson()
    {
        try
        {
            return string.IsNullOrWhiteSpace(CredentialsBase64)
                ? null
                : System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(CredentialsBase64.Trim()));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
