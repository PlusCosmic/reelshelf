using System.Text.RegularExpressions;

namespace Reelshelf.Video.Transcoding.Overflow;

/// <summary>
/// The overflow pool (<c>Overflow:*</c>, docs/video-pipeline/workers-and-jobs.md#the-overflow-pool): one-shot transcode
/// workers that the API starts when <c>transcode_run</c> backs up, on the <see cref="Backend"/>: ECS (AWS Fargate Spot,
/// or an ECS EC2 capacity provider, with <c>RunTask</c>) or Google Cloud Batch (<see cref="Batch"/>). The workers stop
/// themselves once idle, so the API only ever starts them. For ECS, <see cref="Cluster"/>,
/// <see cref="TaskDefinition"/>, <see cref="CapacityProvider"/>, <see cref="Subnets"/> and <see cref="SecurityGroups"/>
/// come from the infrastructure stack's outputs; for Batch, PlusCosmic/infrastructure's gcp/overflow-workers/setup.sh
/// prints the settings.
/// </summary>
public sealed partial class OverflowOptions
{
    public const string SectionName = "Overflow";

    /// <summary><see cref="Backend"/>: tasks on ECS with <c>RunTask</c>.</summary>
    public const string EcsBackend = "ecs";

    /// <summary><see cref="Backend"/>: jobs on Google Cloud Batch.</summary>
    public const string BatchBackend = "batch";

    /// <summary>The longest GCP label value, which <see cref="StartedBy"/> is on the Batch backend.</summary>
    public const int MaxGcpLabelLength = 63;

    /// <summary>The capacity provider overflow tasks are launched on unless <see cref="CapacityProvider"/> says otherwise.</summary>
    public const string DefaultCapacityProvider = "FARGATE_SPOT";

    /// <summary>The capacity providers that are Fargate, whose tasks need an <c>awsvpcConfiguration</c>.</summary>
    public static readonly IReadOnlyList<string> FargateCapacityProviders = ["FARGATE_SPOT", "FARGATE"];

    /// <summary>The longest <c>startedBy</c> ECS accepts.</summary>
    public const int MaxStartedByLength = 128;

    private static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(5);

    /// <summary>Off by default; on, startup fails unless the chosen backend's settings are all set.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Where the workers run: <c>ecs</c> (the default) or <c>batch</c>. Only that backend's settings are needed; the
    /// scaling settings below apply to both.
    /// </summary>
    public string Backend { get; set; } = EcsBackend;

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

    /// <summary>
    /// The <c>startedBy</c> tag on every ECS task the API starts, or the <c>pool</c> label on every Batch job, by which
    /// it counts them again.
    /// </summary>
    public string StartedBy { get; set; } = "reelshelf-overflow";

    /// <summary>The ECS cluster's name or ARN.</summary>
    public string Cluster { get; set; } = "";

    /// <summary>
    /// The worker's task definition (family, <c>family:revision</c> or ARN). Its roles, secrets and environment are
    /// used as they are.
    /// </summary>
    public string TaskDefinition { get; set; } = "";

    /// <summary>
    /// The capacity provider every overflow task is launched on. <c>FARGATE_SPOT</c> (the default) or <c>FARGATE</c>
    /// runs the task in <see cref="Subnets"/> with a public IP; any other name is taken as an EC2 capacity provider on
    /// the cluster, whose task definition uses bridge networking, so no network configuration is sent.
    /// </summary>
    public string CapacityProvider { get; set; } = DefaultCapacityProvider;

    /// <summary>
    /// Comma-separated subnet ids the tasks run in; tasks get a public IP to reach the API and R2. Fargate only: an
    /// EC2 capacity provider's instances are already in their subnets.
    /// </summary>
    public string Subnets { get; set; } = "";

    /// <summary>Comma-separated security group ids for the tasks. Fargate only, like <see cref="Subnets"/>.</summary>
    public string SecurityGroups { get; set; } = "";

    public OverflowAwsOptions Aws { get; set; } = new();

    public OverflowBatchOptions Batch { get; set; } = new();

    public bool IsBatch => string.Equals(Backend.Trim(), BatchBackend, StringComparison.OrdinalIgnoreCase);

    public TimeSpan EffectiveInterval => Interval < MinimumInterval ? MinimumInterval : Interval;

    public IReadOnlyList<string> JobTypeList => SplitList(JobTypes);

    public IReadOnlyList<string> SubnetList => SplitList(Subnets);

    public IReadOnlyList<string> SecurityGroupList => SplitList(SecurityGroups);

    /// <summary>Whether <see cref="CapacityProvider"/> is Fargate (awsvpc networking) rather than EC2 (bridge).</summary>
    public bool IsFargate => FargateCapacityProviders.Contains(CapacityProvider.Trim());

    /// <summary>
    /// What is wrong with these settings for an enabled pool, if anything: an unknown backend; for ECS a missing
    /// cluster, task definition, capacity provider or region, missing subnets or security groups on Fargate, or half a
    /// key pair; for Batch whatever <see cref="OverflowBatchOptions.Problems"/> finds; and for both unknown job types or
    /// limits below 1.
    /// </summary>
    public IReadOnlyList<string> Problems()
    {
        List<string> problems = [];
        void Require(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                problems.Add($"{SectionName}:{name} is missing");
            }
        }

        if (IsBatch)
        {
            problems.AddRange(Batch.Problems());
            if (!GcpLabelValue().IsMatch(StartedBy.Trim()))
            {
                problems.Add($"{SectionName}:{nameof(StartedBy)} must be 1 to {MaxGcpLabelLength} lowercase letters, digits, - or _ on the Batch backend (it is a label)");
            }
        }
        else if (string.Equals(Backend.Trim(), EcsBackend, StringComparison.OrdinalIgnoreCase))
        {
            Require(Cluster, nameof(Cluster));
            Require(TaskDefinition, nameof(TaskDefinition));
            Require(CapacityProvider, nameof(CapacityProvider));
            if (IsFargate)
            {
                Require(Subnets, nameof(Subnets));
                Require(SecurityGroups, nameof(SecurityGroups));
            }

            Require(Aws.Region, $"{nameof(Aws)}:{nameof(OverflowAwsOptions.Region)}");

            bool hasKey = !string.IsNullOrWhiteSpace(Aws.AccessKeyId);
            bool hasSecret = !string.IsNullOrWhiteSpace(Aws.SecretAccessKey);
            if (hasKey != hasSecret)
            {
                problems.Add($"{SectionName}:Aws needs both AccessKeyId and SecretAccessKey, or neither for the default credential chain");
            }

            if (string.IsNullOrWhiteSpace(StartedBy) || StartedBy.Trim().Length > MaxStartedByLength)
            {
                problems.Add($"{SectionName}:{nameof(StartedBy)} must be 1 to {MaxStartedByLength} characters");
            }
        }
        else
        {
            problems.Add($"{SectionName}:{nameof(Backend)} must be {EcsBackend} or {BatchBackend}");
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
/// How the API reaches ECS. <see cref="Region"/> is the AWS project's selected Region. Without keys the SDK's default
/// credential chain is used (environment, profile, instance role).
/// </summary>
public sealed class OverflowAwsOptions
{
    public string Region { get; set; } = "";
    public string AccessKeyId { get; set; } = "";
    public string SecretAccessKey { get; set; } = "";
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
