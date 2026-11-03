using Amazon.ECS;
using Amazon.ECS.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Reelshelf.Video.Transcoding.Overflow;
using Xunit;
using Task = System.Threading.Tasks.Task;
using TaskDefinition = Amazon.ECS.Model.TaskDefinition;

namespace Reelshelf.Test.Video;

/// <summary>
/// The overflow pool on AWS Fargate Spot: the <c>RunTask</c> request <see cref="EcsOverflowTaskLauncher"/> sends, how
/// it counts tasks, and how it registers a task definition revision for the configured worker image (against a fake
/// ECS).
/// </summary>
public class EcsOverflowTests
{
    private const string Image = "ghcr.io/pluscosmic/reelshelf-worker:sha-new";
    private const string OldImage = "ghcr.io/pluscosmic/reelshelf-worker:sha-old";

    [Fact]
    public void RunTaskRequest_LaunchesOnFargateSpotWithAPublicIp()
    {
        OverflowOptions options = Settings();

        RunTaskRequest request = EcsOverflowTaskLauncher.BuildRunTaskRequest(options, "arn:td:7", 3);

        Assert.Equal("reelshelf-overflow-prod", request.Cluster);
        Assert.Equal("arn:td:7", request.TaskDefinition);
        Assert.Equal(3, request.Count);
        Assert.Equal("reelshelf-overflow", request.StartedBy);
        Assert.Null(request.LaunchType);
        CapacityProviderStrategyItem strategy = Assert.Single(request.CapacityProviderStrategy);
        Assert.Equal("FARGATE_SPOT", strategy.CapacityProvider);
        Assert.Equal(["subnet-a", "subnet-b"], request.NetworkConfiguration.AwsvpcConfiguration.Subnets);
        Assert.Equal(["sg-1"], request.NetworkConfiguration.AwsvpcConfiguration.SecurityGroups);
        Assert.Equal(AssignPublicIp.ENABLED, request.NetworkConfiguration.AwsvpcConfiguration.AssignPublicIp);
        // The task definition's roles, secret and environment are left alone.
        Assert.Null(request.Overrides);
        // No tags: the API's user has no ecs:TagResource, and startedBy is how the tasks are found again.
        Assert.True(request.Tags is null or { Count: 0 });
        Assert.False(string.IsNullOrEmpty(request.ClientToken));
        Assert.NotEqual(request.ClientToken, EcsOverflowTaskLauncher.BuildRunTaskRequest(options, "arn:td:7", 3).ClientToken);
    }

    [Fact]
    public async Task Counting_TakesRunningAndPendingTasksOnce()
    {
        FakeEcs ecs = new();
        ecs.Tasks[DesiredStatus.RUNNING] = ["a", "b"];
        ecs.Tasks[DesiredStatus.PENDING] = ["b", "c"];

        Assert.Equal(3, await Launcher(ecs).CountActiveTasksAsync(CancellationToken.None));
        Assert.All(ecs.ListCalls, call => Assert.Equal(("reelshelf-overflow-prod", "reelshelf-overflow"), call));
    }

    [Fact]
    public async Task ANewImage_RegistersARevisionWithIt_OnEveryContainerOfTheWorkersRepository()
    {
        FakeEcs ecs = new() { Latest = Definition(OldImage, revision: 4) };
        EcsOverflowTaskLauncher launcher = Launcher(ecs);

        Assert.Equal(2, await launcher.StartTasksAsync(2, CancellationToken.None));

        RegisterTaskDefinitionRequest register = Assert.Single(ecs.Registered);
        Assert.Equal("reelshelf-worker-overflow-prod", register.Family);
        Assert.Equal(["volume-init", "worker", "sidecar"], register.ContainerDefinitions.Select(c => c.Name));
        Assert.Equal([Image, Image, "public.ecr.aws/other:1"], register.ContainerDefinitions.Select(c => c.Image));
        // Everything else is copied from the latest revision.
        Assert.Equal("2048", register.Cpu);
        Assert.Equal("4096", register.Memory);
        Assert.Equal(NetworkMode.Awsvpc, register.NetworkMode);
        Assert.Equal(["FARGATE"], register.RequiresCompatibilities);
        Assert.Equal("arn:execution", register.ExecutionRoleArn);
        Assert.Equal(40, register.EphemeralStorage.SizeInGiB);
        Assert.Equal(["work", "tmp"], register.Volumes.Select(v => v.Name));
        Assert.Equal("Worker__Secret", Assert.Single(register.ContainerDefinitions[1].Secrets).Name);
        Assert.Equal(120, register.ContainerDefinitions[1].StopTimeout);
        Assert.Equal("arn:td:5", Assert.Single(ecs.Runs).TaskDefinition);

        // The revision is remembered for the next start.
        await launcher.StartTasksAsync(1, CancellationToken.None);
        Assert.Equal(1, ecs.DescribeCalls);
        Assert.Single(ecs.Registered);
        Assert.Equal("arn:td:5", ecs.Runs[1].TaskDefinition);
    }

    [Fact]
    public async Task TheLatestRevision_AlreadyOnTheImage_IsUsedAsItIs()
    {
        FakeEcs ecs = new() { Latest = Definition(Image, revision: 9) };

        await Launcher(ecs).StartTasksAsync(1, CancellationToken.None);

        Assert.Empty(ecs.Registered);
        Assert.Equal("arn:td:9", Assert.Single(ecs.Runs).TaskDefinition);
    }

    [Fact]
    public async Task WithoutAWorkerImage_RunsTheFamilyAsItIs()
    {
        FakeEcs ecs = new();

        await Launcher(ecs, o => o.Ecs.WorkerImage = "").StartTasksAsync(1, CancellationToken.None);

        Assert.Equal(0, ecs.DescribeCalls);
        Assert.Equal("reelshelf-worker-overflow-prod", Assert.Single(ecs.Runs).TaskDefinition);
    }

    [Fact]
    public async Task AFailedRunTask_ForgetsTheRevision_AndThrowsWhenNothingStarted()
    {
        FakeEcs ecs = new() { Latest = Definition(Image, revision: 9), RunError = new AmazonECSException("TaskDefinition is inactive") };
        EcsOverflowTaskLauncher launcher = Launcher(ecs);

        await Assert.ThrowsAsync<AmazonECSException>(() => launcher.StartTasksAsync(1, CancellationToken.None));

        ecs.RunError = null;
        await launcher.StartTasksAsync(1, CancellationToken.None);
        Assert.Equal(2, ecs.DescribeCalls);
    }

    [Fact]
    public async Task NoFargateSpotCapacity_StartsWhatItCan_AndReportsTheRest()
    {
        FakeEcs ecs = new() { Latest = Definition(Image, revision: 1), Accept = 1 };

        Assert.Equal(1, await Launcher(ecs).StartTasksAsync(3, CancellationToken.None));
        Assert.Equal(3, Assert.Single(ecs.Runs).Count);
    }

    [Fact]
    public async Task MoreThanTenTasks_GoInSeveralRunTaskCalls()
    {
        FakeEcs ecs = new() { Latest = Definition(Image, revision: 1) };

        Assert.Equal(12, await Launcher(ecs).StartTasksAsync(12, CancellationToken.None));
        Assert.Equal([10, 2], ecs.Runs.Select(run => run.Count ?? 0));
    }

    [Fact]
    public void RunsImage_LooksOnlyAtTheWorkersRepository()
    {
        Assert.True(EcsOverflowTaskLauncher.RunsImage(Definition(Image, 1), Image));
        Assert.False(EcsOverflowTaskLauncher.RunsImage(Definition(OldImage, 1), Image));
        Assert.False(EcsOverflowTaskLauncher.RunsImage(new TaskDefinition { ContainerDefinitions = [] }, Image));
    }

    [Fact]
    public void ARevisionWithoutTheWorkersRepository_IsRefused()
    {
        TaskDefinition other = new()
        {
            TaskDefinitionArn = "arn:td:1",
            ContainerDefinitions = [new ContainerDefinition { Name = "x", Image = "nginx:1" }]
        };

        Assert.Throws<InvalidOperationException>(() => EcsOverflowTaskLauncher.BuildRegisterRequest(other, Image));
    }

    [Theory]
    [InlineData("ghcr.io/pluscosmic/reelshelf-worker:sha-abc", "ghcr.io/pluscosmic/reelshelf-worker")]
    [InlineData("ghcr.io/pluscosmic/reelshelf-worker@sha256:00", "ghcr.io/pluscosmic/reelshelf-worker")]
    [InlineData("registry:5000/worker", "registry:5000/worker")]
    [InlineData("registry:5000/worker:1", "registry:5000/worker")]
    public void ImageRepository_DropsTheTagOrDigest(string image, string repository)
    {
        Assert.Equal(repository, EcsOverflowTaskLauncher.ImageRepository(image));
    }

    [Fact]
    public void BatchsProvisionTimeout_DefaultsToThreeMinutes()
    {
        // Short, so a Batch job stuck on quota stops holding the pool's places before long.
        Assert.Equal(TimeSpan.FromMinutes(3), new OverflowBatchOptions().ProvisionTimeout);
    }

    private static OverflowOptions Settings(Action<OverflowOptions>? configure = null)
    {
        OverflowOptions options = new()
        {
            Enabled = true,
            Pools = "ecs",
            Ecs = new OverflowEcsOptions
            {
                Region = "eu-north-1",
                Cluster = "reelshelf-overflow-prod",
                TaskDefinition = "reelshelf-worker-overflow-prod",
                Subnets = "subnet-a, subnet-b",
                SecurityGroups = "sg-1",
                WorkerImage = Image
            }
        };
        configure?.Invoke(options);
        return options;
    }

    private static EcsOverflowTaskLauncher Launcher(FakeEcs ecs, Action<OverflowOptions>? configure = null) =>
        new(ecs, Options.Create(Settings(configure)), NullLogger<EcsOverflowTaskLauncher>.Instance);

    private static TaskDefinition Definition(string image, int revision) => new()
    {
        TaskDefinitionArn = $"arn:td:{revision}",
        Family = "reelshelf-worker-overflow-prod",
        Revision = revision,
        Cpu = "2048",
        Memory = "4096",
        NetworkMode = NetworkMode.Awsvpc,
        RequiresCompatibilities = ["FARGATE"],
        ExecutionRoleArn = "arn:execution",
        EphemeralStorage = new EphemeralStorage { SizeInGiB = 40 },
        Volumes = [new Volume { Name = "work" }, new Volume { Name = "tmp" }],
        ContainerDefinitions =
        [
            new ContainerDefinition { Name = "volume-init", Image = image, Essential = false },
            new ContainerDefinition
            {
                Name = "worker",
                Image = image,
                StopTimeout = 120,
                Secrets = [new Secret { Name = "Worker__Secret", ValueFrom = "arn:secret" }]
            },
            new ContainerDefinition { Name = "sidecar", Image = "public.ecr.aws/other:1" }
        ]
    };

    private sealed class FakeEcs : IEcsTasks
    {
        public Dictionary<DesiredStatus, List<string>> Tasks { get; } = [];
        public List<(string Cluster, string StartedBy)> ListCalls { get; } = [];
        public TaskDefinition Latest { get; set; } = new();
        public int DescribeCalls { get; private set; }
        public List<RegisterTaskDefinitionRequest> Registered { get; } = [];
        public List<RunTaskRequest> Runs { get; } = [];
        public int Accept { get; set; } = int.MaxValue;
        public Exception? RunError { get; set; }

        public Task<IReadOnlyList<string>> ListTaskArnsAsync(string cluster, string startedBy,
            DesiredStatus desiredStatus, CancellationToken cancellationToken)
        {
            ListCalls.Add((cluster, startedBy));
            return Task.FromResult<IReadOnlyList<string>>(Tasks.GetValueOrDefault(desiredStatus) ?? []);
        }

        public Task<TaskDefinition> DescribeLatestAsync(string family, CancellationToken cancellationToken)
        {
            DescribeCalls++;
            return Task.FromResult(Latest);
        }

        public Task<TaskDefinition> RegisterAsync(RegisterTaskDefinitionRequest request,
            CancellationToken cancellationToken)
        {
            Registered.Add(request);
            return Task.FromResult(new TaskDefinition
            {
                TaskDefinitionArn = $"arn:td:{Latest.Revision + 1}",
                Family = request.Family
            });
        }

        public Task<RunTaskResponse> RunTaskAsync(RunTaskRequest request, CancellationToken cancellationToken)
        {
            if (RunError is not null)
            {
                return Task.FromException<RunTaskResponse>(RunError);
            }

            Runs.Add(request);
            int accepted = Math.Min(request.Count ?? 0, Accept);
            return Task.FromResult(new RunTaskResponse
            {
                Tasks = Enumerable.Range(0, accepted).Select(i => new Amazon.ECS.Model.Task { TaskArn = $"t{i}" }).ToList(),
                Failures = accepted < request.Count ? [new Failure { Reason = "Capacity is unavailable at this time" }] : []
            });
        }
    }
}
