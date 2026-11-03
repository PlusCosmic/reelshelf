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

    public async Task<TaskDefinition> DescribeLatestAsync(string family, CancellationToken cancellationToken) =>
        (await ecs.DescribeTaskDefinitionAsync(new DescribeTaskDefinitionRequest { TaskDefinition = family },
            cancellationToken)).TaskDefinition;

    public async Task<TaskDefinition> RegisterAsync(RegisterTaskDefinitionRequest request,
        CancellationToken cancellationToken) =>
        (await ecs.RegisterTaskDefinitionAsync(request, cancellationToken)).TaskDefinition;

    public Task<RunTaskResponse> RunTaskAsync(RunTaskRequest request, CancellationToken cancellationToken) =>
        ecs.RunTaskAsync(request, cancellationToken);

    /// <summary>
    /// An ECS client in <see cref="OverflowEcsOptions.Region"/>, with the configured keys, or the SDK's default
    /// credential chain when there are none.
    /// </summary>
    public static IAmazonECS CreateClient(OverflowEcsOptions settings)
    {
        RegionEndpoint region = RegionEndpoint.GetBySystemName(settings.Region.Trim());
        return string.IsNullOrWhiteSpace(settings.AccessKeyId)
            ? new AmazonECSClient(region)
            : new AmazonECSClient(
                new BasicAWSCredentials(settings.AccessKeyId.Trim(), settings.SecretAccessKey.Trim()), region);
    }
}

/// <summary>
/// The overflow pool on AWS Fargate Spot (docs/video-pipeline/fargate-overflow.md): ECS tasks of
/// <see cref="OverflowEcsOptions.TaskDefinition"/>, one worker each, with <c>startedBy</c> =
/// <see cref="OverflowOptions.StartedBy"/> so <c>ListTasks</c> finds them again. Fargate Spot sends SIGTERM two minutes
/// before it takes a task back, and the task definition's <c>stopTimeout</c> covers the worker's shutdown grace and
/// release, so the worker needs no preemption watcher here.
/// </summary>
public sealed class EcsOverflowTaskLauncher(
    IEcsTasks ecs,
    IOptions<OverflowOptions> options,
    ILogger<EcsOverflowTaskLauncher> logger) : IOverflowTaskLauncher
{
    /// <summary>The most tasks one <c>RunTask</c> call can start.</summary>
    public const int MaxTasksPerRunTask = 10;

    public const string CapacityProvider = "FARGATE_SPOT";

    private readonly SemaphoreSlim _resolving = new(1, 1);

    /// <summary>The revision last found or registered for an image, so each start doesn't describe it again.</summary>
    private (string Image, string Arn)? _revision;

    public async Task<int> CountActiveTasksAsync(CancellationToken cancellationToken)
    {
        OverflowOptions current = options.Value;
        // A task that is provisioning or starting has desired status RUNNING; PENDING is listed too, as ECS allows it.
        HashSet<string> tasks = [];
        foreach (DesiredStatus status in new[] { DesiredStatus.RUNNING, DesiredStatus.PENDING })
        {
            tasks.UnionWith(await ecs.ListTaskArnsAsync(current.Ecs.Cluster.Trim(), current.StartedBy.Trim(), status,
                cancellationToken));
        }

        return tasks.Count;
    }

    public async Task<int> StartTasksAsync(int count, CancellationToken cancellationToken)
    {
        OverflowOptions current = options.Value;
        string taskDefinition = await TaskDefinitionForAsync(current.Ecs, cancellationToken);
        int started = 0;
        while (started < count)
        {
            int batch = Math.Min(MaxTasksPerRunTask, count - started);
            RunTaskResponse response;
            try
            {
                response = await ecs.RunTaskAsync(BuildRunTaskRequest(current, taskDefinition, batch), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The revision may have been deregistered; find it again next time.
                _revision = null;
                if (started == 0)
                {
                    throw;
                }

                logger.LogWarning(ex, "Started {Started} Fargate overflow tasks, then ECS refused the next {Batch}",
                    started, batch);
                return started;
            }

            int accepted = response.Tasks?.Count ?? 0;
            started += accepted;
            foreach (Failure failure in response.Failures ?? [])
            {
                // No spare Fargate Spot capacity (or quota) shows up here rather than as an error.
                logger.LogWarning("ECS couldn't start a Fargate overflow task: {Reason} {Detail}", failure.Reason,
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
    /// The task definition to run: the family as it is without <see cref="OverflowEcsOptions.WorkerImage"/>, else the
    /// latest revision when it already runs that image, else a new revision copied from it with the image swapped.
    /// </summary>
    private async Task<string> TaskDefinitionForAsync(OverflowEcsOptions settings, CancellationToken cancellationToken)
    {
        string family = settings.TaskDefinition.Trim();
        string image = settings.WorkerImage.Trim();
        if (image.Length == 0)
        {
            return family;
        }

        if (_revision is { } known && known.Image == image)
        {
            return known.Arn;
        }

        await _resolving.WaitAsync(cancellationToken);
        try
        {
            if (_revision is { } raced && raced.Image == image)
            {
                return raced.Arn;
            }

            TaskDefinition latest = await ecs.DescribeLatestAsync(family, cancellationToken);
            string arn;
            if (RunsImage(latest, image))
            {
                arn = latest.TaskDefinitionArn;
            }
            else
            {
                TaskDefinition registered = await ecs.RegisterAsync(BuildRegisterRequest(latest, image), cancellationToken);
                arn = registered.TaskDefinitionArn;
                logger.LogInformation("Registered {TaskDefinition} for the Fargate overflow workers' image {Image}",
                    arn, image);
            }

            _revision = (image, arn);
            return arn;
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
    /// A copy of <paramref name="latest"/> (size, network mode, roles, volumes, ephemeral storage, platform and every
    /// container's settings) with <paramref name="image"/> on each container of the same repository, such as the
    /// worker and its volume-init container. Tags aren't copied: the API's user has no <c>ecs:TagResource</c>.
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
            IpcMode = latest.IpcMode
        };
    }

    /// <summary>
    /// One <c>RunTask</c> call for <paramref name="count"/> tasks on Fargate Spot, in the configured subnets with a
    /// public IP, so in a public subnet they reach the API and R2 without a NAT gateway. The client token makes the
    /// SDK's own retries of this call idempotent.
    /// </summary>
    public static RunTaskRequest BuildRunTaskRequest(OverflowOptions options, string taskDefinition, int count) => new()
    {
        Cluster = options.Ecs.Cluster.Trim(),
        TaskDefinition = taskDefinition,
        Count = count,
        StartedBy = options.StartedBy.Trim(),
        ClientToken = Guid.NewGuid().ToString("N"),
        CapacityProviderStrategy = [new CapacityProviderStrategyItem { CapacityProvider = CapacityProvider, Weight = 1 }],
        NetworkConfiguration = new NetworkConfiguration
        {
            AwsvpcConfiguration = new AwsVpcConfiguration
            {
                Subnets = options.Ecs.SubnetList.ToList(),
                SecurityGroups = options.Ecs.SecurityGroupList.ToList(),
                AssignPublicIp = AssignPublicIp.ENABLED
            }
        }
    };

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
