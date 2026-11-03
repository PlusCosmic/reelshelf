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
/// The overflow pools on ECS, Fargate Spot (<c>ecs</c>) and EC2 Spot (<c>ec2</c>): the <c>RunTask</c> request
/// <see cref="EcsOverflowTaskLauncher"/> sends for each network mode, how it counts tasks, and how it registers a task
/// definition revision for the configured worker image (against a fake ECS).
/// </summary>
public class EcsOverflowTests
{
    private const string Image = "ghcr.io/pluscosmic/reelshelf-worker:sha-new";
    private const string OldImage = "ghcr.io/pluscosmic/reelshelf-worker:sha-old";

    [Fact]
    public void RunTaskRequest_LaunchesOnFargateSpotWithAPublicIp()
    {
        OverflowOptions options = Settings();

        RunTaskRequest request = EcsOverflowTaskLauncher.BuildRunTaskRequest(options.StartedBy, options.Ecs, Awsvpc, 3);

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
        Assert.NotEqual(request.ClientToken,
            EcsOverflowTaskLauncher.BuildRunTaskRequest(options.StartedBy, options.Ecs, Awsvpc, 3).ClientToken);
    }

    [Fact]
    public void TheFargatePool_DefaultsToFargateSpot_AndTakesAnotherCapacityProviderFromSettings()
    {
        Assert.Equal("FARGATE_SPOT", new OverflowEcsOptions().CapacityProvider);

        OverflowOptions options = Settings(o => o.Ecs.CapacityProvider = "FARGATE");
        RunTaskRequest request = EcsOverflowTaskLauncher.BuildRunTaskRequest(options.StartedBy, options.Ecs, Awsvpc, 1);

        Assert.Equal("FARGATE", Assert.Single(request.CapacityProviderStrategy).CapacityProvider);
        Assert.Equal(AssignPublicIp.ENABLED, request.NetworkConfiguration.AwsvpcConfiguration.AssignPublicIp);
    }

    [Theory]
    [InlineData("host")]
    [InlineData("bridge")]
    [InlineData(null)]
    public void RunTaskRequest_OnEc2_InHostOrBridgeMode_SendsNoNetworkConfiguration(string? networkMode)
    {
        OverflowOptions options = Settings();

        RunTaskRequest request = EcsOverflowTaskLauncher.BuildRunTaskRequest(options.StartedBy, options.Ec2,
            new RunnableTaskDefinition("arn:ec2td:3", networkMode is null ? null : new NetworkMode(networkMode)), 2);

        Assert.Equal("reelshelf-overflow-ec2-prod", request.Cluster);
        Assert.Equal("arn:ec2td:3", request.TaskDefinition);
        Assert.Equal(2, request.Count);
        Assert.Equal("reelshelf-overflow", request.StartedBy);
        Assert.Null(request.LaunchType);
        CapacityProviderStrategyItem strategy = Assert.Single(request.CapacityProviderStrategy);
        Assert.Equal("reelshelf-overflow-ec2-spot", strategy.CapacityProvider);
        Assert.Null(request.NetworkConfiguration);
        Assert.Null(request.Overrides);
    }

    [Fact]
    public void RunTaskRequest_OnEc2_InAwsvpcMode_SendsTheSubnetsWithoutAPublicIp()
    {
        // EC2 refuses assignPublicIp for awsvpc tasks; such a pool needs a NAT.
        OverflowOptions options = Settings(o =>
        {
            o.Ec2.Subnets = "subnet-c";
            o.Ec2.SecurityGroups = "sg-2, sg-3";
        });

        RunTaskRequest request = EcsOverflowTaskLauncher.BuildRunTaskRequest(options.StartedBy, options.Ec2, Awsvpc, 1);

        Assert.Equal(["subnet-c"], request.NetworkConfiguration.AwsvpcConfiguration.Subnets);
        Assert.Equal(["sg-2", "sg-3"], request.NetworkConfiguration.AwsvpcConfiguration.SecurityGroups);
        Assert.Null(request.NetworkConfiguration.AwsvpcConfiguration.AssignPublicIp);
    }

    [Fact]
    public void RunTaskRequest_InAwsvpcMode_WithoutSubnets_IsRefused()
    {
        OverflowOptions options = Settings();

        Assert.Contains("awsvpc", Assert.Throws<InvalidOperationException>(() =>
            EcsOverflowTaskLauncher.BuildRunTaskRequest(options.StartedBy, options.Ec2, Awsvpc, 1)).Message);
    }

    [Fact]
    public async Task TheEc2Pool_RunsOnItsOwnClusterAndCapacityProvider_InTheTaskDefinitionsNetworkMode()
    {
        FakeEcs ecs = new() { Latest = Ec2Definition(Image, revision: 2) };
        ecs.Tasks[DesiredStatus.RUNNING] = ["a"];
        EcsOverflowTaskLauncher launcher = Launcher(ecs, pool: OverflowOptions.Ec2Pool);

        Assert.Equal(1, await launcher.CountActiveTasksAsync(CancellationToken.None));
        Assert.Equal(3, await launcher.StartTasksAsync(3, CancellationToken.None));

        Assert.All(ecs.ListCalls, call => Assert.Equal(("reelshelf-overflow-ec2-prod", "reelshelf-overflow"), call));
        Assert.Equal("reelshelf-worker-overflow-ec2-prod", ecs.DescribedFamily);
        RunTaskRequest run = Assert.Single(ecs.Runs);
        Assert.Equal("arn:td:2", run.TaskDefinition);
        Assert.Equal("reelshelf-overflow-ec2-prod", run.Cluster);
        Assert.Equal("reelshelf-overflow-ec2-spot", Assert.Single(run.CapacityProviderStrategy).CapacityProvider);
        Assert.Null(run.NetworkConfiguration);
    }

    [Fact]
    public async Task AnEc2TaskDefinition_IsCopiedForANewImage_WithoutWhatFargateNeeds()
    {
        FakeEcs ecs = new() { Latest = Ec2Definition(OldImage, revision: 2) };

        await Launcher(ecs, pool: OverflowOptions.Ec2Pool).StartTasksAsync(1, CancellationToken.None);

        RegisterTaskDefinitionRequest register = Assert.Single(ecs.Registered);
        Assert.Equal("reelshelf-worker-overflow-ec2-prod", register.Family);
        Assert.Equal(NetworkMode.Host, register.NetworkMode);
        Assert.Equal(["EC2"], register.RequiresCompatibilities);
        // An EC2 task definition may leave the task's size to its containers.
        Assert.Null(register.Cpu);
        Assert.Null(register.Memory);
        Assert.Null(register.EphemeralStorage);
        Assert.Equal("memberOf", Assert.Single(register.PlacementConstraints).Type.Value);
        Assert.Equal([Image], register.ContainerDefinitions.Select(c => c.Image));
        Assert.Equal(3584, register.ContainerDefinitions[0].MemoryReservation);
        RunTaskRequest run = Assert.Single(ecs.Runs);
        Assert.Equal("arn:td:3", run.TaskDefinition);
        // The registered revision's network mode decides: host, so no network configuration.
        Assert.Null(run.NetworkConfiguration);
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
    public async Task Counting_StopsTasksStillProvisioningPastTheTimeout_AndLeavesThemOut()
    {
        FakeEcs ecs = new();
        ecs.Tasks[DesiredStatus.RUNNING] = ["stuck", "booting", "working"];
        ecs.Described.AddRange([
            EcsTask("stuck", "PROVISIONING", Now - TimeSpan.FromMinutes(5)),
            EcsTask("booting", "PROVISIONING", Now - TimeSpan.FromMinutes(2)),
            EcsTask("working", "RUNNING", Now - TimeSpan.FromMinutes(30))
        ]);

        int active = await Launcher(ecs, pool: OverflowOptions.Ec2Pool).CountActiveTasksAsync(CancellationToken.None);

        Assert.Equal(2, active);
        Assert.Equal([("reelshelf-overflow-ec2-prod", "stuck")], ecs.Stopped);
    }

    [Fact]
    public async Task ATaskThatCantBeStopped_StillCounts()
    {
        FakeEcs ecs = new() { FailStops = true };
        ecs.Tasks[DesiredStatus.RUNNING] = ["stuck"];
        ecs.Described.Add(EcsTask("stuck", "PROVISIONING", Now - TimeSpan.FromMinutes(5)));

        Assert.Equal(1, await Launcher(ecs, pool: OverflowOptions.Ec2Pool).CountActiveTasksAsync(CancellationToken.None));
    }

    [Fact]
    public async Task WhenTasksCantBeDescribed_TheyAllCount()
    {
        FakeEcs ecs = new() { FailDescribes = true };
        ecs.Tasks[DesiredStatus.RUNNING] = ["a", "b"];

        Assert.Equal(2, await Launcher(ecs).CountActiveTasksAsync(CancellationToken.None));
        Assert.Empty(ecs.Stopped);
    }

    [Fact]
    public async Task AfterTasksGotNoCapacity_ThePoolStartsNothing_UntilTheTimeoutHasPassedAgain()
    {
        FakeEcs ecs = new() { Latest = Ec2Definition(Image, revision: 3) };
        ecs.Tasks[DesiredStatus.RUNNING] = ["stuck"];
        ecs.Described.Add(EcsTask("stuck", "PROVISIONING", Now - TimeSpan.FromMinutes(4)));
        MutableTime time = new(Now);
        EcsOverflowTaskLauncher launcher = Launcher(ecs, pool: OverflowOptions.Ec2Pool, time: time);

        Assert.Equal(0, await launcher.CountActiveTasksAsync(CancellationToken.None));

        // Started nothing, so the scaler offers the tasks to the next pool.
        Assert.Equal(0, await launcher.StartTasksAsync(2, CancellationToken.None));
        time.Now = Now + TimeSpan.FromMinutes(3);
        Assert.Equal(0, await launcher.StartTasksAsync(2, CancellationToken.None));
        Assert.Empty(ecs.Runs);

        time.Now = Now + TimeSpan.FromMinutes(4);
        Assert.Equal(2, await launcher.StartTasksAsync(2, CancellationToken.None));
    }

    [Fact]
    public void NeverStarted_OnlyATaskStillProvisioningPastTheTimeout()
    {
        TimeSpan timeout = TimeSpan.FromMinutes(4);
        Assert.True(EcsOverflowTaskLauncher.NeverStarted(EcsTask("a", "PROVISIONING", Now - timeout), Now, timeout));
        Assert.False(EcsOverflowTaskLauncher.NeverStarted(EcsTask("a", "PROVISIONING", Now - TimeSpan.FromMinutes(3)), Now, timeout));
        Assert.False(EcsOverflowTaskLauncher.NeverStarted(EcsTask("a", "PENDING", Now - TimeSpan.FromHours(1)), Now, timeout));
        Assert.False(EcsOverflowTaskLauncher.NeverStarted(EcsTask("a", "RUNNING", Now - TimeSpan.FromHours(1)), Now, timeout));
        Assert.False(EcsOverflowTaskLauncher.NeverStarted(new Amazon.ECS.Model.Task { LastStatus = "PROVISIONING" }, Now, timeout));

        // The SDK may hand the time back as local time.
        Amazon.ECS.Model.Task local = new() { LastStatus = "PROVISIONING", CreatedAt = (Now - timeout).LocalDateTime };
        Assert.True(EcsOverflowTaskLauncher.NeverStarted(local, Now, timeout));
    }

    [Fact]
    public void TheEcsPoolsProvisionTimeout_DefaultsToFourMinutes()
    {
        // Above an EC2 instance's 1 to 3 minute cold start.
        Assert.Equal(TimeSpan.FromMinutes(4), new OverflowEc2Options().ProvisionTimeout);
        Assert.Equal(TimeSpan.FromMinutes(4), new OverflowEcsOptions().ProvisionTimeout);
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
        FakeEcs ecs = new() { Latest = Definition(OldImage, revision: 4) };
        EcsOverflowTaskLauncher launcher = Launcher(ecs, o => o.Ecs.WorkerImage = "");

        await launcher.StartTasksAsync(1, CancellationToken.None);
        await launcher.StartTasksAsync(1, CancellationToken.None);

        // Described once, for its network mode, and never registered.
        Assert.Equal(1, ecs.DescribeCalls);
        Assert.Empty(ecs.Registered);
        Assert.Equal("reelshelf-worker-overflow-prod", ecs.Runs[0].TaskDefinition);
        Assert.Equal(["subnet-a", "subnet-b"], ecs.Runs[0].NetworkConfiguration.AwsvpcConfiguration.Subnets);
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
            },
            Ec2 = new OverflowEc2Options
            {
                Region = "eu-north-1",
                Cluster = "reelshelf-overflow-ec2-prod",
                CapacityProvider = "reelshelf-overflow-ec2-spot",
                TaskDefinition = "reelshelf-worker-overflow-ec2-prod",
                WorkerImage = Image
            }
        };
        configure?.Invoke(options);
        return options;
    }

    private static readonly RunnableTaskDefinition Awsvpc = new("arn:td:7", NetworkMode.Awsvpc);

    private static readonly DateTimeOffset Now = new(2026, 11, 3, 16, 0, 0, TimeSpan.Zero);

    private static EcsOverflowTaskLauncher Launcher(FakeEcs ecs, Action<OverflowOptions>? configure = null,
        string pool = OverflowOptions.EcsPool, TimeProvider? time = null) =>
        new(pool, ecs, Options.Create(Settings(configure)), time ?? new MutableTime(Now),
            NullLogger<EcsOverflowTaskLauncher>.Instance);

    private static Amazon.ECS.Model.Task EcsTask(string arn, string lastStatus, DateTimeOffset createdAt) =>
        new() { TaskArn = arn, LastStatus = lastStatus, CreatedAt = createdAt.UtcDateTime };

    private sealed class MutableTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>The EC2 pool's shape: host networking, sized by its container, one task per instance.</summary>
    private static TaskDefinition Ec2Definition(string image, int revision) => new()
    {
        TaskDefinitionArn = $"arn:td:{revision}",
        Family = "reelshelf-worker-overflow-ec2-prod",
        Revision = revision,
        NetworkMode = NetworkMode.Host,
        RequiresCompatibilities = ["EC2"],
        ExecutionRoleArn = "arn:execution",
        PlacementConstraints =
        [
            new TaskDefinitionPlacementConstraint
            {
                Type = TaskDefinitionPlacementConstraintType.MemberOf,
                Expression = "attribute:ecs.instance-type =~ c7a.*"
            }
        ],
        ContainerDefinitions =
        [
            new ContainerDefinition
            {
                Name = "worker",
                Image = image,
                MemoryReservation = 3584,
                StopTimeout = 120,
                Secrets = [new Secret { Name = "Worker__Secret", ValueFrom = "arn:secret" }]
            }
        ]
    };

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
        public string? DescribedFamily { get; private set; }
        public List<RegisterTaskDefinitionRequest> Registered { get; } = [];
        public List<RunTaskRequest> Runs { get; } = [];
        public int Accept { get; set; } = int.MaxValue;
        public Exception? RunError { get; set; }
        public List<Amazon.ECS.Model.Task> Described { get; } = [];
        public List<(string Cluster, string Task)> Stopped { get; } = [];
        public bool FailStops { get; init; }
        public bool FailDescribes { get; init; }

        public Task<IReadOnlyList<string>> ListTaskArnsAsync(string cluster, string startedBy,
            DesiredStatus desiredStatus, CancellationToken cancellationToken)
        {
            ListCalls.Add((cluster, startedBy));
            return Task.FromResult<IReadOnlyList<string>>(Tasks.GetValueOrDefault(desiredStatus) ?? []);
        }

        public Task<IReadOnlyList<Amazon.ECS.Model.Task>> DescribeTasksAsync(string cluster,
            IReadOnlyList<string> taskArns, CancellationToken cancellationToken) =>
            FailDescribes
                ? Task.FromException<IReadOnlyList<Amazon.ECS.Model.Task>>(new InvalidOperationException("Not authorized"))
                : Task.FromResult<IReadOnlyList<Amazon.ECS.Model.Task>>(
                    Described.Where(task => taskArns.Contains(task.TaskArn)).ToList());

        public Task StopTaskAsync(string cluster, string taskArn, string reason, CancellationToken cancellationToken)
        {
            if (FailStops)
            {
                throw new InvalidOperationException("ECS said no");
            }

            Stopped.Add((cluster, taskArn));
            return Task.CompletedTask;
        }

        public Task<TaskDefinition> DescribeLatestAsync(string family, CancellationToken cancellationToken)
        {
            DescribeCalls++;
            DescribedFamily = family;
            return Task.FromResult(Latest);
        }

        public Task<TaskDefinition> RegisterAsync(RegisterTaskDefinitionRequest request,
            CancellationToken cancellationToken)
        {
            Registered.Add(request);
            return Task.FromResult(new TaskDefinition
            {
                TaskDefinitionArn = $"arn:td:{Latest.Revision + 1}",
                Family = request.Family,
                NetworkMode = request.NetworkMode
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
