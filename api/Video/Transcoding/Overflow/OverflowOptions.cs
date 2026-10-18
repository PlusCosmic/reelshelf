namespace Reelshelf.Video.Transcoding.Overflow;

/// <summary>
/// The overflow pool (<c>Overflow:*</c>, docs/video-pipeline/workers-and-jobs.md#the-overflow-pool): one-shot transcode
/// workers on AWS Fargate Spot that the API starts with ECS <c>RunTask</c> when <c>transcode_run</c> backs up. The
/// workers stop themselves once idle, so the API only ever starts tasks. <see cref="Cluster"/>,
/// <see cref="TaskDefinition"/>, <see cref="Subnets"/> and <see cref="SecurityGroups"/> come from the infrastructure
/// stack's outputs.
/// </summary>
public sealed class OverflowOptions
{
    public const string SectionName = "Overflow";

    /// <summary>The capacity provider every overflow task is launched on.</summary>
    public const string CapacityProvider = "FARGATE_SPOT";

    /// <summary>The longest <c>startedBy</c> ECS accepts.</summary>
    public const int MaxStartedByLength = 128;

    private static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(5);

    /// <summary>Off by default; on, startup fails unless the AWS settings below are all set.</summary>
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

    /// <summary>The <c>startedBy</c> tag on every task the API starts, by which it counts them again.</summary>
    public string StartedBy { get; set; } = "reelshelf-overflow";

    /// <summary>The ECS cluster's name or ARN.</summary>
    public string Cluster { get; set; } = "";

    /// <summary>
    /// The worker's task definition (family, <c>family:revision</c> or ARN). Its roles, secrets and environment are
    /// used as they are.
    /// </summary>
    public string TaskDefinition { get; set; } = "";

    /// <summary>Comma-separated subnet ids the tasks run in; tasks get a public IP to reach the API and R2.</summary>
    public string Subnets { get; set; } = "";

    /// <summary>Comma-separated security group ids for the tasks.</summary>
    public string SecurityGroups { get; set; } = "";

    public OverflowAwsOptions Aws { get; set; } = new();

    public TimeSpan EffectiveInterval => Interval < MinimumInterval ? MinimumInterval : Interval;

    public IReadOnlyList<string> JobTypeList => SplitList(JobTypes);

    public IReadOnlyList<string> SubnetList => SplitList(Subnets);

    public IReadOnlyList<string> SecurityGroupList => SplitList(SecurityGroups);

    /// <summary>
    /// What is wrong with these settings for an enabled pool, if anything: a missing cluster, task definition,
    /// subnets, security groups or region, half a key pair, unknown job types, or limits below 1.
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

        Require(Cluster, nameof(Cluster));
        Require(TaskDefinition, nameof(TaskDefinition));
        Require(Subnets, nameof(Subnets));
        Require(SecurityGroups, nameof(SecurityGroups));
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
