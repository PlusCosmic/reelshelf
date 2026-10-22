using Google.Cloud.Batch.V1;
using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Reelshelf.Video.Transcoding;
using Reelshelf.Video.Transcoding.Overflow;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Reelshelf.Test.Video;

/// <summary>
/// The overflow pool on Google Cloud Batch: the job <see cref="BatchOverflowTaskLauncher"/> creates, how it counts
/// tasks and clears jobs that never start (against a fake Batch), the backend's startup checks, and the transcode
/// queue's presign lifetime now that <c>Transcoding:StaleAfter</c> is short.
/// </summary>
public class BatchOverflowTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Job_RunsTheWorkerFromTheTemplate_WithItsSecret_RetryingOnPreemption()
    {
        OverflowOptions options = Options(Settings());

        Job job = BatchOverflowTaskLauncher.BuildJob(options, 3);

        TaskGroup group = Assert.Single(job.TaskGroups);
        Assert.Equal(3, group.TaskCount);
        Assert.Equal(3, group.Parallelism);
        TaskSpec spec = group.TaskSpec;
        Assert.Equal("ghcr.io/pluscosmic/reelshelf-worker:sha-abc", Assert.Single(spec.Runnables).Container.ImageUri);
        Assert.Equal("https://reelshelf.example", spec.Environment.Variables["Worker__ApiBaseUrl"]);
        Assert.Equal("4", spec.Environment.Variables["Worker__LogicalProcessors"]);
        Assert.Equal("00:01:00", spec.Environment.Variables["Worker__ExitWhenIdleAfter"]);
        Assert.Equal("gce", spec.Environment.Variables["Worker__PreemptionNotice"]);
        // The secret comes from Secret Manager, never as a plain variable.
        Assert.False(spec.Environment.Variables.ContainsKey("Worker__Secret"));
        Assert.Equal("projects/p/secrets/reelshelf-worker-gcp/versions/latest", spec.Environment.SecretVariables["Worker__Secret"]);
        Assert.Equal(4000, spec.ComputeResource.CpuMilli);
        Assert.Equal(6144, spec.ComputeResource.MemoryMib);
        Assert.Equal(Duration.FromTimeSpan(TimeSpan.FromHours(12)), spec.MaxRunDuration);
        Assert.Equal(BatchOverflowTaskLauncher.MaxRetryCount, spec.MaxRetryCount);
        LifecyclePolicy retry = Assert.Single(spec.LifecyclePolicies);
        Assert.Equal(LifecyclePolicy.Types.Action.RetryTask, retry.Action);
        Assert.Equal([50001], retry.ActionCondition.ExitCodes);

        AllocationPolicy allocation = job.AllocationPolicy;
        Assert.Equal(["regions/europe-west9"], allocation.Location.AllowedLocations);
        // A template and no instance policy: only the template gives the VMs a Standard-tier IP.
        AllocationPolicy.Types.InstancePolicyOrTemplate instances = Assert.Single(allocation.Instances);
        Assert.Equal("projects/p/global/instanceTemplates/reelshelf-overflow-c4-v1", instances.InstanceTemplate);
        Assert.Null(instances.Policy);
        Assert.Equal("worker@p.iam.gserviceaccount.com", allocation.ServiceAccount.Email);
        Assert.Equal("reelshelf-overflow", allocation.Labels["pool"]);
        Assert.Equal(LogsPolicy.Types.Destination.CloudLogging, job.LogsPolicy.Destination);
    }

    [Fact]
    public void JobIds_AreValidBatchIds_AndDiffer()
    {
        string id = BatchOverflowTaskLauncher.NewJobId(Now);

        Assert.Matches("^overflow-20261022-160000-[0-9a-f]{4}$", id);
        Assert.Matches("^[a-z]([a-z0-9-]{0,61}[a-z0-9])?$", id);
        Assert.NotEqual(id, BatchOverflowTaskLauncher.NewJobId(Now));
    }

    [Fact]
    public void TheFilter_FindsThePoolsUnfinishedJobs_ByTheAllocationLabel()
    {
        Assert.Equal(
            "allocation_policy.labels.pool=\"reelshelf-overflow\" AND status.state!=\"SUCCEEDED\" AND status.state!=\"FAILED\"",
            BatchOverflowTaskLauncher.ActiveJobsFilter(Options(Settings())));
    }

    [Fact]
    public void ActiveTasks_AreTheAskedForLessTheFinished()
    {
        // Just created: no status yet, so every task counts.
        Assert.Equal(4, BatchOverflowTaskLauncher.ActiveTasks(JobOf(4, JobStatus.Types.State.Queued)));
        Assert.Equal(2, BatchOverflowTaskLauncher.ActiveTasks(
            JobOf(4, JobStatus.Types.State.Running, ("RUNNING", 2), ("SUCCEEDED", 1), ("FAILED", 1))));
        // A preempted task waiting to be retried is pending, and still counts.
        Assert.Equal(3, BatchOverflowTaskLauncher.ActiveTasks(
            JobOf(3, JobStatus.Types.State.Running, ("RUNNING", 2), ("PENDING", 1))));
        Assert.Equal(0, BatchOverflowTaskLauncher.ActiveTasks(JobOf(4, JobStatus.Types.State.Succeeded, ("SUCCEEDED", 4))));
        Assert.Equal(0, BatchOverflowTaskLauncher.ActiveTasks(JobOf(4, JobStatus.Types.State.DeletionInProgress)));
    }

    [Fact]
    public void NeverStarted_OnlyAnOldJobWithNoTaskEverStarted()
    {
        TimeSpan timeout = TimeSpan.FromMinutes(10);
        Job stuck = JobOf(2, JobStatus.Types.State.Scheduled, ("PENDING", 2));
        stuck.CreateTime = Timestamp.FromDateTimeOffset(Now - TimeSpan.FromMinutes(11));
        Assert.True(BatchOverflowTaskLauncher.NeverStarted(stuck, Now, timeout));

        Job young = stuck.Clone();
        young.CreateTime = Timestamp.FromDateTimeOffset(Now - TimeSpan.FromMinutes(9));
        Assert.False(BatchOverflowTaskLauncher.NeverStarted(young, Now, timeout));

        Job partly = JobOf(2, JobStatus.Types.State.Scheduled, ("PENDING", 1), ("RUNNING", 1));
        partly.CreateTime = stuck.CreateTime;
        Assert.False(BatchOverflowTaskLauncher.NeverStarted(partly, Now, timeout));

        Job running = JobOf(2, JobStatus.Types.State.Running, ("PENDING", 2));
        running.CreateTime = stuck.CreateTime;
        Assert.False(BatchOverflowTaskLauncher.NeverStarted(running, Now, timeout));
    }

    [Fact]
    public async Task Counting_DeletesJobsThatNeverStarted_AndCountsTheRest()
    {
        FakeBatch batch = new();
        Job stuck = JobOf(3, JobStatus.Types.State.Scheduled, ("PENDING", 3));
        stuck.Name = "projects/p/locations/europe-west9/jobs/stuck";
        stuck.CreateTime = Timestamp.FromDateTimeOffset(Now - TimeSpan.FromMinutes(30));
        stuck.Status.StatusEvents.Add(new StatusEvent { Description = "Quota CPUS_PER_VM_FAMILY exceeded" });
        Job working = JobOf(2, JobStatus.Types.State.Running, ("RUNNING", 2));
        working.Name = "projects/p/locations/europe-west9/jobs/working";
        working.CreateTime = stuck.CreateTime;
        batch.Jobs.AddRange([stuck, working]);

        int active = await Launcher(batch).CountActiveTasksAsync(CancellationToken.None);

        Assert.Equal(2, active);
        Assert.Equal(["projects/p/locations/europe-west9/jobs/stuck"], batch.Deleted);
        Assert.Equal("projects/p/locations/europe-west9", batch.ListedParent);
        Assert.Contains("allocation_policy.labels.pool=\"reelshelf-overflow\"", batch.ListedFilter);
    }

    [Fact]
    public async Task AJobThatCantBeDeleted_StillCounts()
    {
        FakeBatch batch = new() { FailDeletes = true };
        Job stuck = JobOf(3, JobStatus.Types.State.Scheduled);
        stuck.Name = "projects/p/locations/europe-west9/jobs/stuck";
        stuck.CreateTime = Timestamp.FromDateTimeOffset(Now - TimeSpan.FromMinutes(30));
        batch.Jobs.Add(stuck);

        Assert.Equal(3, await Launcher(batch).CountActiveTasksAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Starting_CreatesOneJobForAllTheWorkers()
    {
        FakeBatch batch = new();

        int started = await Launcher(batch).StartTasksAsync(5, CancellationToken.None);

        Assert.Equal(5, started);
        CreateJobRequest request = Assert.Single(batch.Created);
        Assert.Equal("projects/p/locations/europe-west9", request.Parent);
        Assert.StartsWith("overflow-20261022-160000-", request.JobId);
        Assert.Equal(5, Assert.Single(request.Job.TaskGroups).TaskCount);
        Assert.True(Guid.TryParse(request.RequestId, out _));
    }

    [Fact]
    public void Setup_Batch_RegistersTheBatchLauncher()
    {
        IServiceCollection services = Build(Settings());

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IHostedService) &&
                                                descriptor.ImplementationType == typeof(OverflowBackgroundService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IOverflowTaskLauncher) &&
                                                descriptor.ImplementationType == typeof(BatchOverflowTaskLauncher));
        // No ECS settings needed.
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(Amazon.ECS.IAmazonECS));
    }

    [Theory]
    [InlineData("Overflow:Batch:ProjectId")]
    [InlineData("Overflow:Batch:Region")]
    [InlineData("Overflow:Batch:InstanceTemplate")]
    [InlineData("Overflow:Batch:ServiceAccount")]
    [InlineData("Overflow:Batch:WorkerImage")]
    [InlineData("Overflow:Batch:WorkerSecret")]
    [InlineData("Overflow:Batch:ApiBaseUrl")]
    public void Setup_Batch_FailsWithoutARequiredSetting(string key)
    {
        Dictionary<string, string?> settings = Settings();
        settings[key] = " ";

        Assert.Contains($"{key} is missing", Assert.Throws<InvalidOperationException>(() => Build(settings)).Message);
    }

    [Theory]
    [InlineData("Overflow:Backend", "gke", "Overflow:Backend must be ecs or batch")]
    [InlineData("Overflow:StartedBy", "Reelshelf Overflow", "StartedBy must be 1 to 63 lowercase letters")]
    [InlineData("Overflow:Batch:ApiBaseUrl", "reelshelf.example", "ApiBaseUrl must be an absolute http(s) URL")]
    [InlineData("Overflow:Batch:Vcpus", "0", "Vcpus and MemoryMib must be at least 1")]
    [InlineData("Overflow:Batch:ProvisionTimeout", "00:00:00", "ProvisionTimeout must be positive")]
    [InlineData("Overflow:Batch:CredentialsBase64", "not base64!", "CredentialsBase64 isn't base64")]
    public void Setup_Batch_FailsOnBadSettings(string key, string value, string message)
    {
        Dictionary<string, string?> settings = Settings();
        settings[key] = value;

        Assert.Contains(message, Assert.Throws<InvalidOperationException>(() => Build(settings)).Message);
    }

    [Fact]
    public void Backend_DefaultsToEcs()
    {
        Assert.False(new OverflowOptions().IsBatch);
        Assert.Equal("ecs", new OverflowOptions().Backend);
    }

    [Fact]
    public void PresignedUrls_OutliveTheShortStaleAfter()
    {
        TranscodingOptions defaults = new();
        Assert.Equal(TimeSpan.FromMinutes(5), defaults.StaleAfter);
        Assert.Equal(TimeSpan.FromHours(2), TranscodeQueue.PresignLifetime(defaults));
        Assert.Equal(TimeSpan.FromHours(6), TranscodeQueue.PresignLifetime(new TranscodingOptions { PresignLifetime = TimeSpan.FromHours(6) }));
        // Never shorter than a job can still count as running, plus the hour a late upload gets.
        Assert.Equal(TimeSpan.FromHours(4),
            TranscodeQueue.PresignLifetime(new TranscodingOptions { StaleAfter = TimeSpan.FromHours(3), PresignLifetime = TimeSpan.FromMinutes(30) }));
    }

    private static Dictionary<string, string?> Settings() => new()
    {
        ["Overflow:Enabled"] = "true",
        ["Overflow:Backend"] = "batch",
        ["Overflow:Batch:ProjectId"] = "p",
        ["Overflow:Batch:Region"] = "europe-west9",
        ["Overflow:Batch:InstanceTemplate"] = "projects/p/global/instanceTemplates/reelshelf-overflow-c4-v1",
        ["Overflow:Batch:ServiceAccount"] = "worker@p.iam.gserviceaccount.com",
        ["Overflow:Batch:WorkerImage"] = "ghcr.io/pluscosmic/reelshelf-worker:sha-abc",
        ["Overflow:Batch:WorkerSecret"] = "projects/p/secrets/reelshelf-worker-gcp/versions/latest",
        ["Overflow:Batch:ApiBaseUrl"] = "https://reelshelf.example"
    };

    private static OverflowOptions Options(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build()
            .GetSection(OverflowOptions.SectionName).Get<OverflowOptions>()!;

    private static IServiceCollection Build(Dictionary<string, string?> settings)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddTranscodeOverflow();
        return builder.Services;
    }

    private static BatchOverflowTaskLauncher Launcher(FakeBatch batch) =>
        new(batch, Microsoft.Extensions.Options.Options.Create(Options(Settings())), new FixedTime(Now),
            NullLogger<BatchOverflowTaskLauncher>.Instance);

    private static Job JobOf(int taskCount, JobStatus.Types.State state, params (string State, long Count)[] counts)
    {
        JobStatus.Types.TaskGroupStatus group = new();
        foreach ((string name, long count) in counts)
        {
            group.Counts[name] = count;
        }

        return new Job
        {
            TaskGroups = { new TaskGroup { TaskCount = taskCount } },
            Status = new JobStatus { State = state, TaskGroups = { ["group0"] = group } }
        };
    }

    private sealed class FakeBatch : IBatchJobs
    {
        public List<Job> Jobs { get; } = [];
        public List<string> Deleted { get; } = [];
        public List<CreateJobRequest> Created { get; } = [];
        public bool FailDeletes { get; init; }
        public string? ListedParent { get; private set; }
        public string? ListedFilter { get; private set; }

        public async IAsyncEnumerable<Job> ListAsync(string parent, string filter,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            ListedParent = parent;
            ListedFilter = filter;
            foreach (Job job in Jobs)
            {
                await Task.Yield();
                yield return job;
            }
        }

        public Task CreateAsync(CreateJobRequest request, CancellationToken cancellationToken)
        {
            Created.Add(request);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string name, string reason, CancellationToken cancellationToken)
        {
            if (FailDeletes)
            {
                throw new InvalidOperationException("Batch said no");
            }

            Deleted.Add(name);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
