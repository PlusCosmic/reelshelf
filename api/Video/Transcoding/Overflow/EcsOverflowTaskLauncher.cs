using Amazon;
using Amazon.ECS;
using Amazon.ECS.Model;
using Amazon.Runtime;
using Microsoft.Extensions.Options;

namespace Reelshelf.Video.Transcoding.Overflow;

/// <summary>Starts and counts the overflow pool's tasks; a fake stands in for ECS in tests.</summary>
public interface IOverflowTaskLauncher
{
    /// <summary>The overflow tasks running or still provisioning.</summary>
    Task<int> CountActiveTasksAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Starts up to <paramref name="count"/> tasks and returns how many ECS accepted. Throws when nothing could be
    /// asked of ECS at all.
    /// </summary>
    Task<int> StartTasksAsync(int count, CancellationToken cancellationToken);
}

/// <summary>
/// The overflow pool on ECS: tasks of <see cref="OverflowOptions.TaskDefinition"/> on
/// <see cref="OverflowOptions.CapacityProvider"/>, tagged with <see cref="OverflowOptions.StartedBy"/> so
/// <c>ListTasks</c> finds them again. The task definition's roles, secrets and environment are left as they are; only
/// a Fargate task's network is set here.
/// </summary>
public sealed class EcsOverflowTaskLauncher(
    IAmazonECS ecs,
    IOptions<OverflowOptions> options,
    ILogger<EcsOverflowTaskLauncher> logger) : IOverflowTaskLauncher
{
    /// <summary>The most tasks one <c>RunTask</c> call can start.</summary>
    public const int MaxTasksPerRunTask = 10;

    public async Task<int> CountActiveTasksAsync(CancellationToken cancellationToken)
    {
        OverflowOptions current = options.Value;
        // A task that is provisioning or starting has desired status RUNNING (on EC2 that includes one waiting in
        // PROVISIONING for managed scaling to start an instance); PENDING is listed too, as ECS allows it.
        HashSet<string> tasks = [];
        foreach (DesiredStatus status in new[] { DesiredStatus.RUNNING, DesiredStatus.PENDING })
        {
            string? nextToken = null;
            do
            {
                ListTasksResponse response = await ecs.ListTasksAsync(new ListTasksRequest
                {
                    Cluster = current.Cluster.Trim(),
                    StartedBy = current.StartedBy.Trim(),
                    DesiredStatus = status,
                    NextToken = nextToken
                }, cancellationToken);
                tasks.UnionWith(response.TaskArns ?? []);
                nextToken = response.NextToken;
            } while (!string.IsNullOrEmpty(nextToken));
        }

        return tasks.Count;
    }

    public async Task<int> StartTasksAsync(int count, CancellationToken cancellationToken)
    {
        int started = 0;
        while (started < count)
        {
            int batch = Math.Min(MaxTasksPerRunTask, count - started);
            RunTaskResponse response;
            try
            {
                response = await ecs.RunTaskAsync(BuildRunTaskRequest(options.Value, batch), cancellationToken);
            }
            catch (Exception ex) when (started > 0 && ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Started {Started} overflow tasks, then ECS refused the next {Batch}", started, batch);
                return started;
            }

            int accepted = response.Tasks?.Count ?? 0;
            started += accepted;
            foreach (Failure failure in response.Failures ?? [])
            {
                // A capacity provider without spare capacity shows up here rather than as an error.
                logger.LogWarning("ECS couldn't start an overflow task: {Reason} {Detail}", failure.Reason, failure.Detail);
            }

            if (accepted < batch)
            {
                break;
            }
        }

        return started;
    }

    /// <summary>
    /// One <c>RunTask</c> call for <paramref name="count"/> tasks on <see cref="OverflowOptions.CapacityProvider"/>.
    /// On Fargate the task gets a public IP so, in a public subnet, it reaches the API and R2 without a NAT gateway; on
    /// an EC2 capacity provider no network configuration is sent, since the task definition's bridge mode owns it. The
    /// client token makes the SDK's own retries of this call idempotent.
    /// </summary>
    public static RunTaskRequest BuildRunTaskRequest(OverflowOptions options, int count) => new()
    {
        Cluster = options.Cluster.Trim(),
        TaskDefinition = options.TaskDefinition.Trim(),
        Count = count,
        StartedBy = options.StartedBy.Trim(),
        ClientToken = Guid.NewGuid().ToString("N"),
        CapacityProviderStrategy =
        [
            new CapacityProviderStrategyItem { CapacityProvider = options.CapacityProvider.Trim(), Weight = 1 }
        ],
        NetworkConfiguration = options.IsFargate
            ? new NetworkConfiguration
            {
                AwsvpcConfiguration = new AwsVpcConfiguration
                {
                    Subnets = options.SubnetList.ToList(),
                    SecurityGroups = options.SecurityGroupList.ToList(),
                    AssignPublicIp = AssignPublicIp.ENABLED
                }
            }
            : null
    };

    /// <summary>
    /// An ECS client in <see cref="OverflowAwsOptions.Region"/>, with the configured keys, or the SDK's default
    /// credential chain when there are none.
    /// </summary>
    public static IAmazonECS CreateClient(OverflowAwsOptions aws)
    {
        RegionEndpoint region = RegionEndpoint.GetBySystemName(aws.Region.Trim());
        return string.IsNullOrWhiteSpace(aws.AccessKeyId)
            ? new AmazonECSClient(region)
            : new AmazonECSClient(new BasicAWSCredentials(aws.AccessKeyId.Trim(), aws.SecretAccessKey.Trim()), region);
    }
}
