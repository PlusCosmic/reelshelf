using System.Text.RegularExpressions;

namespace Reelshelf.Video.Transcoding.Overflow;

/// <summary>
/// The overflow pool (<c>Overflow:*</c>, docs/video-pipeline/workers-and-jobs.md#the-overflow-pool): one-shot transcode
/// workers that the API starts when <c>transcode_run</c> backs up, on the backends <see cref="Pools"/> lists, most
/// preferred first: ECS tasks on AWS EC2 Spot instances (<see cref="Ec2"/>) and on Fargate Spot (<see cref="Ecs"/>), and
/// Google Cloud Batch (<see cref="Batch"/>).
/// Each backend has its own cap (its quota), and <see cref="MaxTasks"/> caps them all together. The workers stop
/// themselves once idle, so the API only ever starts them. PlusCosmic/infrastructure's aws/overflow-workers and
/// gcp/overflow-workers give each backend's settings.
/// </summary>
public sealed partial class OverflowOptions
{
    public const string SectionName = "Overflow";

    /// <summary>The pool name of ECS tasks on EC2 Spot instances (<see cref="Ec2"/>).</summary>
    public const string Ec2Pool = "ec2";

    /// <summary>The pool name of ECS tasks on Fargate Spot (<see cref="Ecs"/>).</summary>
    public const string EcsPool = "ecs";

    /// <summary>The pool name of Google Cloud Batch (<see cref="Batch"/>).</summary>
    public const string BatchPool = "batch";

    /// <summary>Every pool <see cref="Pools"/> may name.</summary>
    public static readonly IReadOnlyList<string> KnownPools = [Ec2Pool, EcsPool, BatchPool];

    /// <summary>The pools of ECS tasks, each started by its own <see cref="EcsOverflowTaskLauncher"/>.</summary>
    public static readonly IReadOnlyList<string> EcsPools = [Ec2Pool, EcsPool];

    /// <summary>The longest GCP label value, which <see cref="StartedBy"/> is.</summary>
    public const int MaxGcpLabelLength = 63;

    private static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(5);

    /// <summary>Off by default; on, startup fails unless every pool in <see cref="Pools"/> has all its settings.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The backends workers are started on, comma-separated, most preferred first: <c>batch</c> (the default),
    /// <c>ec2</c>, <c>ecs</c>, or several, e.g. <c>ec2,ecs,batch</c>. Each tick fills the first pool up to its own <c>MaxTasks</c> and
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

    public OverflowEc2Options Ec2 { get; set; } = new();

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
            Ec2Pool => Ec2.MaxTasks,
            EcsPool => Ecs.MaxTasks,
            BatchPool => Batch.MaxTasks,
            _ => 0
        };
        return own > 0 ? own : MaxTasks;
    }

    /// <summary>The settings of an ECS pool (<see cref="EcsPools"/>): <see cref="Ec2"/> or <see cref="Ecs"/>.</summary>
    public OverflowEcsPoolOptions EcsPoolSettings(string pool) => pool switch
    {
        Ec2Pool => Ec2,
        EcsPool => Ecs,
        _ => throw new ArgumentException($"{pool} isn't an ECS overflow pool", nameof(pool))
    };

    /// <summary>
    /// What is wrong with these settings for an enabled pool, if anything: no pools or unknown ones, whatever
    /// <see cref="OverflowEcsPoolOptions.Problems"/> and <see cref="OverflowBatchOptions.Problems"/> find for the pools
    /// in use, two ECS pools on one cluster, a <see cref="StartedBy"/> that isn't a GCP label value, unknown job types,
    /// or limits below 1.
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

        foreach (string pool in EcsPools.Where(pools.Contains))
        {
            problems.AddRange(EcsPoolSettings(pool).Problems());
        }

        // Tasks are counted by cluster and startedBy, so two pools on one cluster would each count the other's.
        if (pools.Contains(Ec2Pool) && pools.Contains(EcsPool) && !string.IsNullOrWhiteSpace(Ec2.Cluster) &&
            Ec2.Cluster.Trim() == Ecs.Cluster.Trim())
        {
            problems.Add($"{SectionName}:{nameof(Ec2)}:{nameof(OverflowEc2Options.Cluster)} and {nameof(Ecs)}:{nameof(OverflowEcsOptions.Cluster)} must be different clusters, as each pool counts the tasks in its own");
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

        if (Ec2.MaxTasks < 0 || Ecs.MaxTasks < 0 || Batch.MaxTasks < 0)
        {
            problems.Add($"{SectionName}:{nameof(Ec2)}:{nameof(OverflowEc2Options.MaxTasks)}, {nameof(Ecs)}:{nameof(OverflowEcsOptions.MaxTasks)} and {nameof(Batch)}:{nameof(OverflowBatchOptions.MaxTasks)} can't be negative (0 means {nameof(MaxTasks)})");
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
    /// counting against <see cref="OverflowOptions.MaxTasks"/>, and the pool starts nothing for as long again.
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
/// An overflow pool of ECS tasks (docs/video-pipeline/fargate-overflow.md, ec2-overflow.md): tasks of
/// <see cref="TaskDefinition"/>, one worker each, started with <c>RunTask</c> on <see cref="CapacityProvider"/> in
/// <see cref="Cluster"/>. The task definition holds the task's size, network mode, roles, secret, volumes and sandbox;
/// the API changes only its image, to <see cref="WorkerImage"/>, by registering a new revision, and sends
/// <see cref="Subnets"/> and <see cref="SecurityGroups"/> only when its network mode is <c>awsvpc</c>.
/// PlusCosmic/infrastructure's aws/overflow-workers (Terraform) creates the resources and outputs these settings.
/// <see cref="OverflowEcsOptions"/> is the Fargate Spot pool and <see cref="OverflowEc2Options"/> the EC2 Spot one;
/// each has its own cluster, so each counts only its own tasks.
/// </summary>
public abstract class OverflowEcsPoolOptions
{
    /// <summary>The pool's section under <c>Overflow</c>, e.g. <c>Ecs</c>.</summary>
    protected abstract string SectionName { get; }

    /// <summary>Whether <see cref="Subnets"/> and <see cref="SecurityGroups"/> must be set (an awsvpc-only pool).</summary>
    protected abstract bool RequiresNetwork { get; }

    private string Prefix => $"{OverflowOptions.SectionName}:{SectionName}:";

    /// <summary>
    /// The most workers of this pool up at once, e.g. its vCPU quota over the task's vCPUs. 0 (the default) leaves
    /// only <see cref="OverflowOptions.MaxTasks"/>.
    /// </summary>
    public int MaxTasks { get; set; }

    /// <summary>The project's Region, e.g. <c>eu-north-1</c>.</summary>
    public string Region { get; set; } = "";

    /// <summary>The ECS cluster's name or ARN; each pool has its own, as tasks are counted per cluster.</summary>
    public string Cluster { get; set; } = "";

    /// <summary>The capacity provider every task is started on, e.g. <c>FARGATE_SPOT</c> or an Auto Scaling group's.</summary>
    public abstract string CapacityProvider { get; set; }

    /// <summary>
    /// The worker's task definition family. Tasks run its latest revision, or the revision the API registers for
    /// <see cref="WorkerImage"/>.
    /// </summary>
    public string TaskDefinition { get; set; } = "";

    /// <summary>
    /// Comma-separated subnet ids the tasks run in, sent only for an <c>awsvpc</c> task definition; on Fargate they
    /// get a public IP to reach the API and R2.
    /// </summary>
    public string Subnets { get; set; } = "";

    /// <summary>Comma-separated security group ids for the tasks, sent only for an <c>awsvpc</c> task definition.</summary>
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

    /// <summary>
    /// A task still <c>PROVISIONING</c> after this long (its capacity provider couldn't place it: no Spot capacity, the
    /// quota, the Auto Scaling group's maximum) is stopped, and the pool starts nothing for as long again, so its
    /// tasks go to the next pool. Above an EC2 instance's cold start (1 to 3 minutes).
    /// </summary>
    public TimeSpan ProvisionTimeout { get; set; } = TimeSpan.FromMinutes(4);

    public IReadOnlyList<string> SubnetList => SplitList(Subnets);

    public IReadOnlyList<string> SecurityGroupList => SplitList(SecurityGroups);

    /// <summary>Whether <see cref="CapacityProvider"/> is Fargate's, the only one where a task can get a public IP.</summary>
    public bool IsFargate => CapacityProvider.Trim() is "FARGATE" or OverflowEcsOptions.FargateSpot;

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
        Require(CapacityProvider, nameof(CapacityProvider));
        Require(TaskDefinition, nameof(TaskDefinition));
        if (RequiresNetwork)
        {
            Require(Subnets, nameof(Subnets));
            Require(SecurityGroups, nameof(SecurityGroups));
        }

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

        if (ProvisionTimeout <= TimeSpan.Zero)
        {
            problems.Add($"{Prefix}{nameof(ProvisionTimeout)} must be positive");
        }

        return problems;
    }

    private static IReadOnlyList<string> SplitList(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();
}

/// <summary>
/// The overflow pool on AWS Fargate Spot (<c>Overflow:Ecs:*</c>, docs/video-pipeline/fargate-overflow.md): an
/// <c>awsvpc</c> task definition, so <see cref="OverflowEcsPoolOptions.Subnets"/> and
/// <see cref="OverflowEcsPoolOptions.SecurityGroups"/> are required, and every task gets a public IP.
/// </summary>
public sealed class OverflowEcsOptions : OverflowEcsPoolOptions
{
    public const string FargateSpot = "FARGATE_SPOT";

    protected override string SectionName => nameof(OverflowOptions.Ecs);

    protected override bool RequiresNetwork => true;

    /// <summary><c>FARGATE_SPOT</c> unless set.</summary>
    public override string CapacityProvider { get; set; } = FargateSpot;
}

/// <summary>
/// The overflow pool on EC2 Spot instances (<c>Overflow:Ec2:*</c>, docs/video-pipeline/ec2-overflow.md): ECS tasks
/// on its own cluster's Auto Scaling group capacity provider (<see cref="CapacityProvider"/>, required), which starts
/// an instance for each and scales back to zero. The task definition is usually <c>host</c> mode, using the instance's
/// public IP, so <see cref="OverflowEcsPoolOptions.Subnets"/> and <see cref="OverflowEcsPoolOptions.SecurityGroups"/>
/// are only needed for an <c>awsvpc</c> one.
/// </summary>
public sealed class OverflowEc2Options : OverflowEcsPoolOptions
{
    protected override string SectionName => nameof(OverflowOptions.Ec2);

    protected override bool RequiresNetwork => false;

    /// <summary>The cluster's Auto Scaling group capacity provider; no default.</summary>
    public override string CapacityProvider { get; set; } = "";
}
