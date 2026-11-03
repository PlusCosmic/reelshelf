using Amazon;
using Amazon.ECS;
using Amazon.ECS.Model;
using Amazon.Runtime;
using Microsoft.Extensions.Options;
using Task = System.Threading.Tasks.Task;
using TaskDefinition = Amazon.ECS.Model.TaskDefinition;

namespace Reelshelf.Video.Transcoding.Overflow;

/// <summary>The few ECS calls the launcher makes; a fake stands in for ECS in tests.</summary>
public interface IEcsTasks
{
    /// <summary>Every task in <paramref name="cluster"/> started by <paramref name="startedBy"/> with that desired status.</summary>
    Task<IReadOnlyList<string>> ListTaskArnsAsync(string cluster, string startedBy, DesiredStatus desiredStatus,
        CancellationToken cancellationToken);

    /// <summary>The tasks <paramref name="taskArns"/> names in <paramref name="cluster"/>, leaving out any ECS no longer has.</summary>
    Task<IReadOnlyList<Amazon.ECS.Model.Task>> DescribeTasksAsync(string cluster, IReadOnlyList<string> taskArns,
        CancellationToken cancellationToken);

    Task StopTaskAsync(string cluster, string taskArn, string reason, CancellationToken cancellationToken);

    /// <summary>The family's latest active revision.</summary>
    Task<TaskDefinition> DescribeLatestAsync(string family, CancellationToken cancellationToken);

    Task<TaskDefinition> RegisterAsync(RegisterTaskDefinitionRequest request, CancellationToken cancellationToken);

    Task<RunTaskResponse> RunTaskAsync(RunTaskRequest request, CancellationToken cancellationToken);
}

/// <summary><see cref="IEcsTasks"/> on the AWS SDK.</summary>
public sealed class AwsEcsTasks(IAmazonECS ecs) : IEcsTasks
{
    public async Task<IReadOnlyList<string>> ListTaskArnsAsync(string cluster, string startedBy,
        DesiredStatus desiredStatus, CancellationToken cancellationToken)
    {
        List<string> arns = [];
        string? nextToken = null;
        do
        {
            ListTasksResponse response = await ecs.ListTasksAsync(new ListTasksRequest
            {
                Cluster = cluster,
                StartedBy = startedBy,
                DesiredStatus = desiredStatus,
                NextToken = nextToken
            }, cancellationToken);
            arns.AddRange(response.TaskArns ?? []);
            nextToken = response.NextToken;
        } while (!string.IsNullOrEmpty(nextToken));

        return arns;
    }

    public async Task<IReadOnlyList<Amazon.ECS.Model.Task>> DescribeTasksAsync(string cluster,
        IReadOnlyList<string> taskArns, CancellationToken cancellationToken)
    {
        List<Amazon.ECS.Model.Task> tasks = [];
        // DescribeTasks takes at most 100 tasks a call.
        foreach (string[] chunk in taskArns.Chunk(100))
        {
            DescribeTasksResponse response = await ecs.DescribeTasksAsync(
                new DescribeTasksRequest { Cluster = cluster, Tasks = [.. chunk] }, cancellationToken);
            tasks.AddRange(response.Tasks ?? []);
        }

        return tasks;
    }

    public async Task StopTaskAsync(string cluster, string taskArn, string reason, CancellationToken cancellationToken) =>
        await ecs.StopTaskAsync(new StopTaskRequest { Cluster = cluster, Task = taskArn, Reason = reason },
            cancellationToken);

    public async Task<TaskDefinition> DescribeLatestAsync(string family, CancellationToken cancellationToken) =>
        (await ecs.DescribeTaskDefinitionAsync(new DescribeTaskDefinitionRequest { TaskDefinition = family },
            cancellationToken)).TaskDefinition;

    public async Task<TaskDefinition> RegisterAsync(RegisterTaskDefinitionRequest request,
        CancellationToken cancellationToken) =>
        (await ecs.RegisterTaskDefinitionAsync(request, cancellationToken)).TaskDefinition;

    public Task<RunTaskResponse> RunTaskAsync(RunTaskRequest request, CancellationToken cancellationToken) =>
        ecs.RunTaskAsync(request, cancellationToken);

    /// <summary>
    /// An ECS client in the pool's <see cref="OverflowEcsPoolOptions.Region"/>, with its keys, or the SDK's default
    /// credential chain when there are none.
    /// </summary>
    public static IAmazonECS CreateClient(OverflowEcsPoolOptions settings)
    {
        RegionEndpoint region = RegionEndpoint.GetBySystemName(settings.Region.Trim());
        return string.IsNullOrWhiteSpace(settings.AccessKeyId)
            ? new AmazonECSClient(region)
            : new AmazonECSClient(
                new BasicAWSCredentials(settings.AccessKeyId.Trim(), settings.SecretAccessKey.Trim()), region);
    }
}

/// <summary>
/// An overflow pool of ECS tasks (docs/video-pipeline/fargate-overflow.md, ec2-overflow.md), <see cref="Pool"/>: the
/// Fargate Spot pool <c>ecs</c> or the EC2 Spot pool <c>ec2</c>, each with its own settings
/// (<see cref="OverflowOptions.EcsPoolSettings"/>), client and cluster. Tasks of the pool's task definition, one worker
/// each, run on its capacity provider with <c>startedBy</c> = <see cref="OverflowOptions.StartedBy"/> so
/// <c>ListTasks</c> finds them again. Fargate Spot sends SIGTERM two minutes before it takes a task back, and an EC2
/// Spot instance's ECS agent drains it on the interruption notice (<c>ECS_ENABLE_SPOT_INSTANCE_DRAINING</c>), which
/// stops its task the same way; the task definition's <c>stopTimeout</c> covers the worker's shutdown grace and
/// release, so the worker needs no preemption watcher here.
/// <para>
/// ECS accepts a task it has no capacity for (an Auto Scaling group that can't launch: no Spot capacity, the vCPU
/// quota, its maximum) and leaves it in <c>PROVISIONING</c>, so <c>RunTask</c> alone never tells the scaler to offer
/// it to the next pool. A task still provisioning after
/// <see cref="OverflowEcsPoolOptions.ProvisionTimeout"/> is stopped while counting, as Batch's jobs are, and the pool
/// starts nothing for as long again, so the next pools take its share; no worker has claimed anything in it.
/// </para>
/// </summary>
public sealed class EcsOverflowTaskLauncher(
    string pool,
    IEcsTasks ecs,
    IOptions<OverflowOptions> options,
    TimeProvider timeProvider,
    ILogger<EcsOverflowTaskLauncher> logger) : IOverflowTaskLauncher
{
    /// <summary>The most tasks one <c>RunTask</c> call can start.</summary>
    public const int MaxTasksPerRunTask = 10;

    /// <summary>The <c>lastStatus</c> of a task waiting for capacity.</summary>
    public const string ProvisioningStatus = "PROVISIONING";

    private readonly SemaphoreSlim _resolving = new(1, 1);

    /// <summary>Until when the pool starts nothing, after tasks had to be stopped for never getting capacity.</summary>
    private DateTimeOffset? _heldBackUntil;

    /// <summary>
    /// The task definition last found or registered for an image ("" when none is set), so each start doesn't describe
    /// it again.
    /// </summary>
    private (string Image, RunnableTaskDefinition Definition)? _revision;

    /// <summary>The pool's name in <see cref="OverflowOptions.Pools"/>.</summary>
    public string Pool { get; } = pool;

    public async Task<int> CountActiveTasksAsync(CancellationToken cancellationToken)
    {
        OverflowOptions current = options.Value;
        OverflowEcsPoolOptions settings = current.EcsPoolSettings(Pool);
        // A task that is provisioning (waiting for an instance) or starting has desired status RUNNING; PENDING is
        // listed too, as ECS allows it.
        string cluster = settings.Cluster.Trim();
        HashSet<string> arns = [];
        foreach (DesiredStatus status in new[] { DesiredStatus.RUNNING, DesiredStatus.PENDING })
        {
            arns.UnionWith(await ecs.ListTaskArnsAsync(cluster, current.StartedBy.Trim(), status, cancellationToken));
        }

        if (arns.Count == 0)
        {
            return 0;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        IReadOnlyList<Amazon.ECS.Model.Task> described;
        try
        {
            described = await ecs.DescribeTasksAsync(cluster, [.. arns], cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The listing still counts (e.g. before the scaler's user has ecs:DescribeTasks); only the timeout waits.
            logger.LogWarning(ex, "Could not describe the {Pool} overflow tasks; counting them all", Pool);
            return arns.Count;
        }

        List<string> stuck = described
            .Where(task => NeverStarted(task, now, settings.ProvisionTimeout))
            .Select(task => task.TaskArn)
            .ToList();
        if (stuck.Count == 0)
        {
            return arns.Count;
        }

        _heldBackUntil = now + settings.ProvisionTimeout;
        logger.LogWarning(
            "Stopping {Count} {Pool} overflow tasks still provisioning after {Timeout} (no capacity on {CapacityProvider}); starting none there until {Until}",
            stuck.Count, Pool, settings.ProvisionTimeout, settings.CapacityProvider.Trim(), _heldBackUntil);
        int stopped = 0;
        foreach (string arn in stuck)
        {
            try
            {
                await ecs.StopTaskAsync(cluster, arn, "Reelshelf: no capacity in time", cancellationToken);
                stopped++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not stop {Pool} overflow task {Task}; it still counts", Pool, arn);
            }
        }

        return arns.Count - stopped;
    }

    /// <summary>
    /// Whether <paramref name="task"/> has waited for capacity longer than <paramref name="timeout"/>: still
    /// <c>PROVISIONING</c> and created that long before <paramref name="now"/>.
    /// </summary>
    public static bool NeverStarted(Amazon.ECS.Model.Task task, DateTimeOffset now, TimeSpan timeout) =>
        task.LastStatus == ProvisioningStatus && task.CreatedAt is { } createdAt &&
        now - AsUtc(createdAt) >= timeout;

    /// <summary>The SDK's timestamp as an instant, taking one of unspecified kind as UTC.</summary>
    private static DateTimeOffset AsUtc(DateTime time) => time.Kind == DateTimeKind.Unspecified
        ? new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Utc))
        : new DateTimeOffset(time.ToUniversalTime());

    public async Task<int> StartTasksAsync(int count, CancellationToken cancellationToken)
    {
        if (_heldBackUntil is { } until && timeProvider.GetUtcNow() < until)
        {
            logger.LogInformation("Starting no {Pool} overflow tasks until {Until}: its last ones got no capacity",
                Pool, until);
            return 0;
        }

        OverflowOptions current = options.Value;
        OverflowEcsPoolOptions settings = current.EcsPoolSettings(Pool);
        RunnableTaskDefinition definition = await TaskDefinitionForAsync(settings, cancellationToken);
        int started = 0;
        while (started < count)
        {
            int batch = Math.Min(MaxTasksPerRunTask, count - started);
            RunTaskResponse response;
            try
            {
                response = await ecs.RunTaskAsync(
                    BuildRunTaskRequest(current.StartedBy, settings, definition, batch), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The revision may have been deregistered, or its network mode changed; find it again next time.
                _revision = null;
                if (started == 0)
                {
                    throw;
                }

                logger.LogWarning(ex, "Started {Started} {Pool} overflow tasks, then ECS refused the next {Batch}",
                    started, Pool, batch);
                return started;
            }

            int accepted = response.Tasks?.Count ?? 0;
            started += accepted;
            foreach (Failure failure in response.Failures ?? [])
            {
                // No spare Spot capacity (or quota) shows up here rather than as an error.
                logger.LogWarning("ECS couldn't start a {Pool} overflow task: {Reason} {Detail}", Pool, failure.Reason,
                    failure.Detail);
            }

            if (accepted < batch)
            {
                break;
            }
        }

        return started;
    }

    /// <summary>
    /// The task definition to run and its network mode: the family's latest revision as it is without
    /// <see cref="OverflowEcsPoolOptions.WorkerImage"/> (run by family), else that revision when it already runs the
    /// image, else a new revision copied from it with the image swapped.
    /// </summary>
    private async Task<RunnableTaskDefinition> TaskDefinitionForAsync(OverflowEcsPoolOptions settings,
        CancellationToken cancellationToken)
    {
        string family = settings.TaskDefinition.Trim();
        string image = settings.WorkerImage.Trim();
        if (_revision is { } known && known.Image == image)
        {
            return known.Definition;
        }

        await _resolving.WaitAsync(cancellationToken);
        try
        {
            if (_revision is { } raced && raced.Image == image)
            {
                return raced.Definition;
            }

            TaskDefinition latest = await ecs.DescribeLatestAsync(family, cancellationToken);
            RunnableTaskDefinition definition;
            if (image.Length == 0)
            {
                definition = new RunnableTaskDefinition(family, latest.NetworkMode);
            }
            else if (RunsImage(latest, image))
            {
                definition = new RunnableTaskDefinition(latest.TaskDefinitionArn, latest.NetworkMode);
            }
            else
            {
                TaskDefinition registered = await ecs.RegisterAsync(BuildRegisterRequest(latest, image), cancellationToken);
                definition = new RunnableTaskDefinition(registered.TaskDefinitionArn,
                    registered.NetworkMode ?? latest.NetworkMode);
                logger.LogInformation("Registered {TaskDefinition} for the {Pool} overflow workers' image {Image}",
                    definition.TaskDefinition, Pool, image);
            }

            _revision = (image, definition);
            return definition;
        }
        finally
        {
            _resolving.Release();
        }
    }

    /// <summary>Whether every container of the image's repository in <paramref name="definition"/> runs <paramref name="image"/>.</summary>
    public static bool RunsImage(TaskDefinition definition, string image)
    {
        string repository = ImageRepository(image);
        List<ContainerDefinition> workers = (definition.ContainerDefinitions ?? [])
            .Where(container => ImageRepository(container.Image ?? "") == repository).ToList();
        return workers.Count > 0 && workers.All(container => container.Image == image);
    }

    /// <summary>
    /// A copy of <paramref name="latest"/> (size, network mode, compatibilities, roles, volumes, ephemeral storage,
    /// platform, placement constraints and every container's settings) with <paramref name="image"/> on each container
    /// of the same repository, such as the worker and its volume-init container. What a Fargate or EC2 task definition
    /// leaves unset (an EC2 one's task-level size, Fargate's placement constraints) stays unset. Tags aren't copied:
    /// the API's user has no <c>ecs:TagResource</c>.
    /// </summary>
    public static RegisterTaskDefinitionRequest BuildRegisterRequest(TaskDefinition latest, string image)
    {
        string repository = ImageRepository(image);
        List<ContainerDefinition> containers = latest.ContainerDefinitions ?? [];
        int swapped = 0;
        foreach (ContainerDefinition container in containers.Where(c => ImageRepository(c.Image ?? "") == repository))
        {
            container.Image = image;
            swapped++;
        }

        if (swapped == 0)
        {
            throw new InvalidOperationException(
                $"Task definition {latest.TaskDefinitionArn} has no container of {repository}, so its image can't be set to {image}");
        }

        return new RegisterTaskDefinitionRequest
        {
            Family = latest.Family,
            ContainerDefinitions = containers,
            Cpu = latest.Cpu,
            Memory = latest.Memory,
            NetworkMode = latest.NetworkMode,
            RequiresCompatibilities = latest.RequiresCompatibilities,
            ExecutionRoleArn = latest.ExecutionRoleArn,
            TaskRoleArn = latest.TaskRoleArn,
            Volumes = latest.Volumes,
            EphemeralStorage = latest.EphemeralStorage,
            RuntimePlatform = latest.RuntimePlatform,
            PidMode = latest.PidMode,
            IpcMode = latest.IpcMode,
            PlacementConstraints = latest.PlacementConstraints,
            ProxyConfiguration = latest.ProxyConfiguration,
            EnableFaultInjection = latest.EnableFaultInjection
        };
    }

    /// <summary>
    /// One <c>RunTask</c> call for <paramref name="count"/> tasks on the pool's capacity provider. Only an
    /// <c>awsvpc</c> task definition gets a network configuration, with the pool's subnets and security groups, and on
    /// Fargate a public IP, so in a public subnet they reach the API and R2 without a NAT gateway (EC2 can't give an
    /// awsvpc task a public IP, so it would need a NAT). <c>host</c> and <c>bridge</c> tasks use their instance's
    /// network. The client token makes the SDK's own retries of this call idempotent.
    /// </summary>
    public static RunTaskRequest BuildRunTaskRequest(string startedBy, OverflowEcsPoolOptions settings,
        RunnableTaskDefinition definition, int count) => new()
    {
        Cluster = settings.Cluster.Trim(),
        TaskDefinition = definition.TaskDefinition,
        Count = count,
        StartedBy = startedBy.Trim(),
        ClientToken = Guid.NewGuid().ToString("N"),
        CapacityProviderStrategy =
            [new CapacityProviderStrategyItem { CapacityProvider = settings.CapacityProvider.Trim(), Weight = 1 }],
        NetworkConfiguration = definition.NetworkMode == NetworkMode.Awsvpc ? AwsvpcNetwork(settings) : null
    };

    private static NetworkConfiguration AwsvpcNetwork(OverflowEcsPoolOptions settings)
    {
        if (settings.SubnetList.Count == 0)
        {
            throw new InvalidOperationException(
                $"The {settings.TaskDefinition.Trim()} task definition uses the awsvpc network mode, which needs the pool's Subnets (and SecurityGroups)");
        }

        return new NetworkConfiguration
        {
            AwsvpcConfiguration = new AwsVpcConfiguration
            {
                Subnets = settings.SubnetList.ToList(),
                SecurityGroups = settings.SecurityGroupList.Count > 0 ? settings.SecurityGroupList.ToList() : null,
                AssignPublicIp = settings.IsFargate ? AssignPublicIp.ENABLED : null
            }
        };
    }

    /// <summary><c>registry/name</c> without its tag or digest, e.g. <c>ghcr.io/pluscosmic/reelshelf-worker</c>.</summary>
    public static string ImageRepository(string image)
    {
        string trimmed = image.Trim();
        int at = trimmed.IndexOf('@');
        if (at >= 0)
        {
            return trimmed[..at];
        }

        int colon = trimmed.LastIndexOf(':');
        return colon > trimmed.LastIndexOf('/') ? trimmed[..colon] : trimmed;
    }
}

/// <summary>
/// What a pool runs: a task definition ARN (or the family, for its latest revision) and its network mode, null when the
/// task definition leaves it to ECS's default (<c>bridge</c> on EC2).
/// </summary>
public sealed record RunnableTaskDefinition(string TaskDefinition, NetworkMode? NetworkMode);
