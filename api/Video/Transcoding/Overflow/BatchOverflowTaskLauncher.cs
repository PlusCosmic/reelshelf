using Google.Api.Gax.ResourceNames;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Batch.V1;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Options;
using BatchEnvironment = Google.Cloud.Batch.V1.Environment;
using Task = System.Threading.Tasks.Task;

namespace Reelshelf.Video.Transcoding.Overflow;

/// <summary>The few Batch calls the launcher makes; a fake stands in for Batch in tests.</summary>
public interface IBatchJobs
{
    /// <summary>The jobs in <paramref name="parent"/> matching <paramref name="filter"/>.</summary>
    IAsyncEnumerable<Job> ListAsync(string parent, string filter, CancellationToken cancellationToken);

    Task CreateAsync(CreateJobRequest request, CancellationToken cancellationToken);

    /// <summary>Asks Batch to delete a job (and its VMs) without waiting for it to finish.</summary>
    Task DeleteAsync(string name, string reason, CancellationToken cancellationToken);
}

/// <summary><see cref="IBatchJobs"/> on Google's client.</summary>
public sealed class GoogleBatchJobs(BatchServiceClient client) : IBatchJobs
{
    public IAsyncEnumerable<Job> ListAsync(string parent, string filter, CancellationToken cancellationToken) =>
        client.ListJobsAsync(new ListJobsRequest { Parent = parent, Filter = filter });

    public async Task CreateAsync(CreateJobRequest request, CancellationToken cancellationToken) =>
        await client.CreateJobAsync(request, cancellationToken);

    public async Task DeleteAsync(string name, string reason, CancellationToken cancellationToken) =>
        await client.DeleteJobAsync(new DeleteJobRequest { Name = name, Reason = reason, RequestId = Guid.NewGuid().ToString() },
            cancellationToken);

    /// <summary>
    /// A Batch client signed in with <see cref="OverflowBatchOptions.CredentialsBase64"/>'s key, or with Application
    /// Default Credentials when it is empty.
    /// </summary>
    public static BatchServiceClient CreateClient(OverflowBatchOptions batch)
    {
        BatchServiceClientBuilder builder = new();
        if (batch.CredentialsJson() is { } json)
        {
            builder.GoogleCredential = CredentialFactory.FromJson<ServiceAccountCredential>(json).ToGoogleCredential();
        }

        return builder.Build();
    }
}

/// <summary>
/// The overflow pool on Google Cloud Batch (docs/video-pipeline/batch-overflow.md): each scale-up is one job of
/// <c>count</c> tasks, one worker VM each, labelled <c>pool</c> = <see cref="OverflowOptions.StartedBy"/> so
/// counting finds them again. A job none of whose tasks has started within
/// <see cref="OverflowBatchOptions.ProvisionTimeout"/> is deleted while counting, so a job stuck on quota or Spot
/// capacity doesn't hold the pool's places for ever; no worker has claimed anything in it.
/// </summary>
public sealed class BatchOverflowTaskLauncher(
    IBatchJobs batch,
    IOptions<OverflowOptions> options,
    TimeProvider timeProvider,
    ILogger<BatchOverflowTaskLauncher> logger) : IOverflowTaskLauncher
{
    /// <summary>The exit code Batch gives a task whose Spot VM was preempted.</summary>
    public const int SpotPreemptionExitCode = 50001;

    /// <summary>The label every pool job (and its VMs) carries, with <see cref="OverflowOptions.StartedBy"/>.</summary>
    public const string PoolLabel = "pool";

    /// <summary>How many times Batch runs a task again, on a preempted VM or a worker that crashed.</summary>
    public const int MaxRetryCount = 2;

    private static readonly string[] FinishedTaskStates = ["SUCCEEDED", "FAILED"];
    private static readonly string[] StartedTaskStates = ["ASSIGNED", "RUNNING", "SUCCEEDED", "FAILED"];

    public async Task<int> CountActiveTasksAsync(CancellationToken cancellationToken)
    {
        OverflowOptions current = options.Value;
        DateTimeOffset now = timeProvider.GetUtcNow();
        int active = 0;
        await foreach (Job job in batch.ListAsync(Parent(current.Batch), ActiveJobsFilter(current), cancellationToken))
        {
            if (NeverStarted(job, now, current.Batch.ProvisionTimeout))
            {
                string reason = job.Status?.StatusEvents.LastOrDefault()?.Description ?? "no status events";
                logger.LogWarning("Deleting overflow job {Job}: none of its tasks started in {Timeout}. Batch said: {Reason}",
                    job.Name, current.Batch.ProvisionTimeout, reason);
                try
                {
                    await batch.DeleteAsync(job.Name, "Reelshelf: no task started in time", cancellationToken);
                    continue;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Could not delete overflow job {Job}; it still counts", job.Name);
                }
            }

            active += ActiveTasks(job);
        }

        return active;
    }

    public async Task<int> StartTasksAsync(int count, CancellationToken cancellationToken)
    {
        OverflowOptions current = options.Value;
        string jobId = NewJobId(timeProvider.GetUtcNow());
        await batch.CreateAsync(new CreateJobRequest
        {
            Parent = Parent(current.Batch),
            JobId = jobId,
            Job = BuildJob(current, count),
            // The SDK may retry the call; the request id makes that create the job once.
            RequestId = Guid.NewGuid().ToString()
        }, cancellationToken);
        logger.LogInformation("Created overflow job {Job} with {Count} workers", jobId, count);
        // Batch takes the whole job or none of it; tasks without capacity show up later as never starting.
        return count;
    }

    /// <summary>
    /// The job for <paramref name="count"/> workers: the worker image with its settings and its secret from Secret
    /// Manager, one task per VM from the instance template, retried on Spot preemption. The workers watch for
    /// preemption themselves (<c>Worker__PreemptionNotice=gce</c>) and hand their job back at once, since Batch stops
    /// the container without SIGTERM's grace.
    /// </summary>
    public static Job BuildJob(OverflowOptions options, int count)
    {
        OverflowBatchOptions settings = options.Batch;
        return new Job
        {
            TaskGroups =
            {
                new TaskGroup
                {
                    TaskCount = count,
                    Parallelism = count,
                    TaskSpec = new TaskSpec
                    {
                        Runnables =
                        {
                            new Runnable { Container = new Runnable.Types.Container { ImageUri = settings.WorkerImage.Trim() } }
                        },
                        Environment = new BatchEnvironment
                        {
                            Variables =
                            {
                                ["Worker__ApiBaseUrl"] = settings.ApiBaseUrl.Trim(),
                                ["Worker__LogicalProcessors"] = settings.Vcpus.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                ["Worker__ExitWhenIdleAfter"] = settings.ExitWhenIdleAfter.ToString("c"),
                                ["Worker__PreemptionNotice"] = "gce"
                            },
                            SecretVariables = { ["Worker__Secret"] = settings.WorkerSecret.Trim() }
                        },
                        ComputeResource = new ComputeResource { CpuMilli = settings.Vcpus * 1000L, MemoryMib = settings.MemoryMib },
                        MaxRunDuration = Duration.FromTimeSpan(settings.MaxRunDuration),
                        MaxRetryCount = MaxRetryCount,
                        LifecyclePolicies =
                        {
                            new LifecyclePolicy
                            {
                                Action = LifecyclePolicy.Types.Action.RetryTask,
                                ActionCondition = new LifecyclePolicy.Types.ActionCondition { ExitCodes = { SpotPreemptionExitCode } }
                            }
                        }
                    }
                }
            },
            AllocationPolicy = new AllocationPolicy
            {
                Location = new AllocationPolicy.Types.LocationPolicy { AllowedLocations = { $"regions/{settings.Region.Trim()}" } },
                // The template, not an instance policy: Batch gives the VMs it describes itself a Premium-tier IP.
                Instances = { new AllocationPolicy.Types.InstancePolicyOrTemplate { InstanceTemplate = settings.InstanceTemplate.Trim() } },
                ServiceAccount = new ServiceAccount { Email = settings.ServiceAccount.Trim() },
                Labels = { [PoolLabel] = options.StartedBy.Trim() }
            },
            LogsPolicy = new LogsPolicy { Destination = LogsPolicy.Types.Destination.CloudLogging }
        };
    }

    /// <summary>
    /// The pool's jobs that haven't finished. Batch filters on the allocation policy's labels, not the job's.
    /// </summary>
    public static string ActiveJobsFilter(OverflowOptions options) =>
        $"allocation_policy.labels.{PoolLabel}=\"{options.StartedBy.Trim()}\" AND status.state!=\"SUCCEEDED\" AND status.state!=\"FAILED\"";

    /// <summary>
    /// The job's tasks that haven't finished, starting ones included: every task the job asked for, less those
    /// succeeded or failed. A job that is over (or being deleted) counts none.
    /// </summary>
    public static int ActiveTasks(Job job)
    {
        if (job.Status?.State is JobStatus.Types.State.Succeeded or JobStatus.Types.State.Failed
            or JobStatus.Types.State.DeletionInProgress)
        {
            return 0;
        }

        long asked = job.TaskGroups.Sum(group => group.TaskCount);
        long finished = (job.Status?.TaskGroups.Values ?? []).Sum(group =>
            FinishedTaskStates.Sum(state => group.Counts.TryGetValue(state, out long n) ? n : 0));
        return (int)Math.Max(0, asked - finished);
    }

    /// <summary>
    /// Whether the job is older than <paramref name="timeout"/> and none of its tasks has started: no VM was found
    /// for it (quota, Spot capacity). Such a job is safe to delete, as no worker has claimed anything in it.
    /// </summary>
    public static bool NeverStarted(Job job, DateTimeOffset now, TimeSpan timeout)
    {
        if (job.CreateTime is null || job.CreateTime.ToDateTimeOffset() > now - timeout)
        {
            return false;
        }

        if (job.Status?.State is not (JobStatus.Types.State.Queued or JobStatus.Types.State.Scheduled))
        {
            return false;
        }

        return !(job.Status.TaskGroups.Values).Any(group =>
            StartedTaskStates.Any(state => group.Counts.TryGetValue(state, out long n) && n > 0));
    }

    /// <summary>
    /// <c>overflow-{yyyyMMdd-HHmmss}-{4 random}</c>: lowercase, under Batch's 63 characters, and short enough that
    /// the VM names Batch derives from it stay readable.
    /// </summary>
    public static string NewJobId(DateTimeOffset now) =>
        $"overflow-{now.UtcDateTime:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..4]}";

    private static string Parent(OverflowBatchOptions settings) =>
        new LocationName(settings.ProjectId.Trim(), settings.Region.Trim()).ToString();
}
