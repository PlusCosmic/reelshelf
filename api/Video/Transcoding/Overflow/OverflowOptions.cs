using System.Text.RegularExpressions;

namespace Reelshelf.Video.Transcoding.Overflow;

/// <summary>
/// The overflow pool (<c>Overflow:*</c>, docs/video-pipeline/workers-and-jobs.md#the-overflow-pool): one-shot transcode
/// workers that the API starts when <c>transcode_run</c> backs up, on the backends <see cref="Pools"/> lists, most
/// preferred first: ECS tasks on AWS Fargate Spot (<see cref="Ecs"/>) and Google Cloud Batch (<see cref="Batch"/>).
/// Each backend has its own cap (its quota), and <see cref="MaxTasks"/> caps them all together. The workers stop
/// themselves once idle, so the API only ever starts them. PlusCosmic/infrastructure's aws/overflow-workers and
/// gcp/overflow-workers give each backend's settings.
/// </summary>
public sealed partial class OverflowOptions
{
    public const string SectionName = "Overflow";

    /// <summary>The pool name of ECS tasks on Fargate Spot (<see cref="Ecs"/>).</summary>
    public const string EcsPool = "ecs";

    /// <summary>The pool name of Google Cloud Batch (<see cref="Batch"/>).</summary>
    public const string BatchPool = "batch";

    /// <summary>Every pool <see cref="Pools"/> may name.</summary>
    public static readonly IReadOnlyList<string> KnownPools = [EcsPool, BatchPool];

    /// <summary>The longest GCP label value, which <see cref="StartedBy"/> is.</summary>
    public const int MaxGcpLabelLength = 63;

    private static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(5);

    /// <summary>Off by default; on, startup fails unless every pool in <see cref="Pools"/> has all its settings.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The backends workers are started on, comma-separated, most preferred first: <c>batch</c> (the default),
    /// <c>ecs</c>, or both, e.g. <c>ecs,batch</c>. Each tick fills the first pool up to its own <c>MaxTasks</c> and
    /// hands the rest to the next; workers a pool can't start (an error, no Spot capacity) go to the next one too.
    /// </summary>
    public string Pools { get; set; } = BatchPool;

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

    /// <summary>
    /// The most overflow tasks running or provisioning at once across every pool: the hard cap on spend. Each pool's
    /// own <c>MaxTasks</c> (its quota) caps it within this.
    /// </summary>
    public int MaxTasks { get; set; } = 4;

    /// <summary>The most tasks one tick starts.</summary>
    public int MaxStartPerTick { get; set; } = 4;

    /// <summary>How long after starting tasks no more are started, so tasks still provisioning aren't double-counted.</summary>
    public TimeSpan Cooldown { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// The <c>pool</c> label on every Batch job and the <c>startedBy</c> of every ECS task the API starts, by which it
    /// counts them again.
    /// </summary>
    public string StartedBy { get; set; } = "reelshelf-overflow";

    public OverflowBatchOptions Batch { get; set; } = new();

    public OverflowEcsOptions Ecs { get; set; } = new();

    public TimeSpan EffectiveInterval => Interval < MinimumInterval ? MinimumInterval : Interval;

    public IReadOnlyList<string> JobTypeList => SplitList(JobTypes);

    /// <summary><see cref="Pools"/> in order, lowercased, without repeats.</summary>
    public IReadOnlyList<string> PoolList => SplitList(Pools.ToLowerInvariant());

    /// <summary>
    /// The most workers <paramref name="pool"/> may have up at once: its own <c>MaxTasks</c>, or
    /// <see cref="MaxTasks"/> while that is unset (0).
    /// </summary>
    public int MaxTasksFor(string pool)
    {
        int own = pool switch
        {
            EcsPool => Ecs.MaxTasks,
            BatchPool => Batch.MaxTasks,
            _ => 0
        };
        return own > 0 ? own : MaxTasks;
    }

    /// <summary>
    /// What is wrong with these settings for an enabled pool, if anything: no pools or unknown ones, whatever
    /// <see cref="OverflowEcsOptions.Problems"/> and <see cref="OverflowBatchOptions.Problems"/> find for the pools in
    /// use, a <see cref="StartedBy"/> that isn't a GCP label value, unknown job types, or limits below 1.
    /// </summary>
    public IReadOnlyList<string> Problems()
    {
        List<string> problems = [];
        IReadOnlyList<string> pools = PoolList;
        if (pools.Count == 0)
        {
            problems.Add($"{SectionName}:{nameof(Pools)} names no pool (use {string.Join(", ", KnownPools)})");
        }

        List<string> unknownPools = pools.Where(pool => !KnownPools.Contains(pool)).ToList();
        if (unknownPools.Count > 0)
        {
            problems.Add($"{SectionName}:{nameof(Pools)} has unknown pools: {string.Join(", ", unknownPools)} (use {string.Join(", ", KnownPools)})");
        }

        if (pools.Contains(EcsPool))
        {
            problems.AddRange(Ecs.Problems());
        }

        if (pools.Contains(BatchPool))
        {
            problems.AddRange(Batch.Problems());
        }

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

        if (Ecs.MaxTasks < 0 || Batch.MaxTasks < 0)
        {
            problems.Add($"{SectionName}:{nameof(Ecs)}:{nameof(OverflowEcsOptions.MaxTasks)} and {nameof(Batch)}:{nameof(OverflowBatchOptions.MaxTasks)} can't be negative (0 means {nameof(MaxTasks)})");
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

    /// <summary>
    /// The most Batch workers up at once, e.g. what the C4 quota holds; 0 (the default) leaves only
    /// <see cref="OverflowOptions.MaxTasks"/>.
    /// </summary>
    public int MaxTasks { get; set; }

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
    public TimeSpan ProvisionTimeout { get; set; } = TimeSpan.FromMinutes(3);

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

/// <summary>
/// The overflow pool on AWS (<c>Overflow:Ecs:*</c>, docs/video-pipeline/fargate-overflow.md): ECS tasks of
/// <see cref="TaskDefinition"/> on Fargate Spot, one worker each, started with <c>RunTask</c> in
/// <see cref="Subnets"/> with a public IP. The task definition holds the task's size, roles, secret, volumes and
/// sandbox; the API changes only its image, to <see cref="WorkerImage"/>, by registering a new revision.
/// PlusCosmic/infrastructure's aws/overflow-workers (Terraform) creates the resources and outputs these settings.
/// </summary>
public sealed class OverflowEcsOptions
{
    private const string Prefix = OverflowOptions.SectionName + ":" + nameof(OverflowOptions.Ecs) + ":";

    /// <summary>
    /// The most Fargate workers up at once: the Fargate Spot vCPU quota over the task's vCPUs (8 / 2 = 4). 0 (the
    /// default) leaves only <see cref="OverflowOptions.MaxTasks"/>.
    /// </summary>
    public int MaxTasks { get; set; }

    /// <summary>The project's Region, e.g. <c>eu-north-1</c>.</summary>
    public string Region { get; set; } = "";

    /// <summary>The ECS cluster's name or ARN.</summary>
    public string Cluster { get; set; } = "";

    /// <summary>
    /// The worker's task definition family. Tasks run its latest revision, or the revision the API registers for
    /// <see cref="WorkerImage"/>.
    /// </summary>
    public string TaskDefinition { get; set; } = "";

    /// <summary>Comma-separated subnet ids the tasks run in, with a public IP to reach the API and R2.</summary>
    public string Subnets { get; set; } = "";

    /// <summary>Comma-separated security group ids for the tasks.</summary>
    public string SecurityGroups { get; set; } = "";

    /// <summary>
    /// The worker image, e.g. <c>ghcr.io/pluscosmic/reelshelf-worker:sha-…</c>, kept current by the bump PR. When the
    /// family's latest revision runs another image, the API registers a copy of it with this one on every container
    /// of the same repository. Empty runs the latest revision as it is.
    /// </summary>
    public string WorkerImage { get; set; } = "";

    /// <summary>The API's access key; empty for both uses the SDK's default credential chain.</summary>
    public string AccessKeyId { get; set; } = "";

    public string SecretAccessKey { get; set; } = "";

    public IReadOnlyList<string> SubnetList => SplitList(Subnets);

    public IReadOnlyList<string> SecurityGroupList => SplitList(SecurityGroups);

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

        Require(Region, nameof(Region));
        Require(Cluster, nameof(Cluster));
        Require(TaskDefinition, nameof(TaskDefinition));
        Require(Subnets, nameof(Subnets));
        Require(SecurityGroups, nameof(SecurityGroups));
        if (!string.IsNullOrWhiteSpace(TaskDefinition) && TaskDefinition.Contains(':'))
        {
            problems.Add($"{Prefix}{nameof(TaskDefinition)} must be the family, without a revision or ARN");
        }

        if (!string.IsNullOrWhiteSpace(WorkerImage) && !WorkerImage.Trim().Contains(':'))
        {
            problems.Add($"{Prefix}{nameof(WorkerImage)} must name a tag, e.g. ghcr.io/pluscosmic/reelshelf-worker:sha-…");
        }

        if (string.IsNullOrWhiteSpace(AccessKeyId) != string.IsNullOrWhiteSpace(SecretAccessKey))
        {
            problems.Add($"{Prefix}{nameof(AccessKeyId)} and {nameof(SecretAccessKey)} must both be set, or neither for the default credential chain");
        }

        return problems;
    }

    private static IReadOnlyList<string> SplitList(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();
}
