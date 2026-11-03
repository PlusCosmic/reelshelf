using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Reelshelf.Video.Transcoding;
using Reelshelf.Video.Transcoding.Overflow;
using Xunit;

namespace Reelshelf.Test.Video;

/// <summary>
/// The overflow pool's decision (<see cref="OverflowScaler"/>) against a fake backlog and fake launchers, one pool or
/// several, and its startup checks (<see cref="OverflowSetup"/>).
/// </summary>
public class OverflowScalerTests
{
    private static readonly TimeSpan Old = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task BelowTheThreshold_StartsNothing()
    {
        Harness test = new();
        test.Backlog.Next = new OverflowBacklog(10, TimeSpan.FromSeconds(119));

        OverflowTick tick = await test.TickAsync();

        Assert.Equal(OverflowTickOutcome.BelowThreshold, tick.Outcome);
        Assert.Empty(test.Launcher.StartRequests);
        // The running tasks are still counted, for the gauge.
        Assert.Equal(0, test.Metrics.Running);
    }

    [Fact]
    public async Task AnEmptyQueue_StartsNothing()
    {
        Harness test = new();
        test.Backlog.Next = OverflowBacklog.Empty;

        Assert.Equal(OverflowTickOutcome.BelowThreshold, (await test.TickAsync()).Outcome);
        Assert.Empty(test.Launcher.StartRequests);
    }

    [Fact]
    public async Task PastTheThreshold_StartsOneTaskPerJobTheRunningOnesDontCover()
    {
        Harness test = new();
        test.Backlog.Next = new OverflowBacklog(3, TimeSpan.FromMinutes(2));
        test.Launcher.Running = 1;

        OverflowTick tick = await test.TickAsync();

        Assert.Equal(OverflowTickOutcome.Started, tick.Outcome);
        Assert.Equal(2, tick.Started);
        Assert.Equal([2], test.Launcher.StartRequests);
        Assert.Equal(3, test.Metrics.Running);
    }

    [Fact]
    public async Task MaxTasks_CapsWhatIsRunning()
    {
        Harness test = new(o => o.MaxTasks = 4);
        test.Backlog.Next = new OverflowBacklog(50, Old);
        test.Launcher.Running = 3;

        Assert.Equal(1, (await test.TickAsync()).Started);

        test.Clock.Advance(TimeSpan.FromMinutes(3));
        test.Launcher.Running = 4;
        OverflowTick full = await test.TickAsync();
        Assert.Equal(OverflowTickOutcome.Enough, full.Outcome);
        Assert.Equal([1], test.Launcher.StartRequests);

        // More running than the cap (the cap was lowered) still starts nothing.
        test.Launcher.Running = 6;
        Assert.Equal(OverflowTickOutcome.Enough, (await test.TickAsync()).Outcome);
    }

    [Fact]
    public async Task MaxStartPerTick_LimitsOneTick()
    {
        Harness test = new(o =>
        {
            o.MaxTasks = 20;
            o.MaxStartPerTick = 3;
        });
        test.Backlog.Next = new OverflowBacklog(50, Old);

        Assert.Equal(3, (await test.TickAsync()).Started);
    }

    [Theory]
    [InlineData(10, 0, 4, 4, 4)]
    [InlineData(2, 0, 4, 4, 2)]
    [InlineData(10, 3, 4, 4, 1)]
    [InlineData(10, 2, 8, 3, 3)]
    [InlineData(1, 2, 4, 4, 0)]
    [InlineData(10, 5, 4, 4, 0)]
    public void TasksToStart_IsTheSmallestOfTheThreeLimits(long pending, int running, int maxTasks, int perTick, int expected)
    {
        OverflowOptions options = new() { MaxTasks = maxTasks, MaxStartPerTick = perTick };
        Assert.Equal(expected, OverflowScaler.TasksToStart(pending, running, options));
    }

    [Fact]
    public async Task AfterStarting_WaitsOutTheCooldown()
    {
        Harness test = new(o =>
        {
            o.MaxTasks = 10;
            o.MaxStartPerTick = 2;
            o.Cooldown = TimeSpan.FromMinutes(2);
        });
        test.Backlog.Next = new OverflowBacklog(10, Old);

        Assert.Equal(2, (await test.TickAsync()).Started);

        // The new tasks may not be listed yet; the cooldown keeps them from being started twice.
        test.Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(OverflowTickOutcome.CoolingDown, (await test.TickAsync()).Outcome);
        test.Clock.Advance(TimeSpan.FromSeconds(89));
        Assert.Equal(OverflowTickOutcome.CoolingDown, (await test.TickAsync()).Outcome);

        test.Clock.Advance(TimeSpan.FromSeconds(1));
        test.Launcher.Running = 2;
        Assert.Equal(2, (await test.TickAsync()).Started);
        Assert.Equal([2, 2], test.Launcher.StartRequests);
    }

    [Fact]
    public async Task NothingStarted_StartsNoCooldown()
    {
        Harness test = new();
        test.Backlog.Next = new OverflowBacklog(10, Old);
        test.Launcher.Accept = 0;

        Assert.Equal(0, (await test.TickAsync()).Started);
        test.Launcher.Accept = int.MaxValue;
        Assert.Equal(4, (await test.TickAsync()).Started);
    }

    [Fact]
    public async Task JobTypes_AreHandedToTheBacklogCount_AndEmptyMeansEveryType()
    {
        Harness all = new();
        await all.TickAsync();
        Assert.Empty(Assert.Single(all.Backlog.Requests).JobTypes);

        Harness some = new(o => o.JobTypes = "transcode, trim,transcode");
        await some.TickAsync();
        Assert.Equal(["transcode", "trim"], Assert.Single(some.Backlog.Requests).JobTypes);
    }

    [Fact]
    public async Task TheBacklogUsesTheClaimsAttemptLimit()
    {
        Harness test = new(transcoding: new TranscodingOptions { MaxAttempts = 5 });
        await test.TickAsync();
        Assert.Equal(5, Assert.Single(test.Backlog.Requests).MaxAttempts);
    }

    [Fact]
    public async Task CountingFailing_IsLoggedAndRetriedOnTheNextTick()
    {
        Harness test = new();
        test.Backlog.Next = new OverflowBacklog(10, Old);
        test.Launcher.ListError = new InvalidOperationException("throttled");

        OverflowTick failed = await test.TickAsync();
        Assert.Equal(OverflowTickOutcome.CountFailed, failed.Outcome);
        Assert.Empty(test.Launcher.StartRequests);
        Assert.Null(test.Metrics.Running);

        test.Launcher.ListError = null;
        Assert.Equal(4, (await test.TickAsync()).Started);
    }

    [Fact]
    public async Task StartingFailing_IsLoggedAndRetriedOnTheNextTick_WithoutACooldown()
    {
        Harness test = new();
        test.Backlog.Next = new OverflowBacklog(10, Old);
        test.Launcher.StartError = new InvalidOperationException("PERMISSION_DENIED");

        Assert.Equal(OverflowTickOutcome.StartFailed, (await test.TickAsync()).Outcome);

        test.Launcher.StartError = null;
        test.Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(4, (await test.TickAsync()).Started);
    }

    [Fact]
    public async Task TheBacklogCountFailing_IsLoggedAndRetried()
    {
        Harness test = new();
        test.Backlog.Error = new InvalidOperationException("database down");

        Assert.Equal(OverflowTickOutcome.CountFailed, (await test.TickAsync()).Outcome);
        Assert.Equal(0, test.Launcher.ListCalls);
    }

    [Fact]
    public async Task Disabled_TouchesNothing()
    {
        Harness test = new(o => o.Enabled = false);

        Assert.Equal(OverflowTickOutcome.Disabled, (await test.TickAsync()).Outcome);
        Assert.Empty(test.Backlog.Requests);
        Assert.Equal(0, test.Launcher.ListCalls);
    }

    [Fact]
    public async Task WithoutR2_TouchesNothing()
    {
        Harness test = new(storageConfigured: false);
        test.Backlog.Next = new OverflowBacklog(10, Old);

        Assert.Equal(OverflowTickOutcome.NoStorage, (await test.TickAsync()).Outcome);
        Assert.Empty(test.Backlog.Requests);
        Assert.Equal(0, test.Launcher.ListCalls);
    }

    [Fact]
    public async Task Metrics_ReportRunningAndStartedTasks()
    {
        Harness test = new();
        test.Backlog.Next = new OverflowBacklog(10, Old);
        test.Launcher.Running = 1;
        List<(string Name, long Value)> recorded = [];
        using MeterListener listener = new();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter == test.Metrics.Meter)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) => recorded.Add((instrument.Name, value)));
        listener.Start();

        listener.RecordObservableInstruments();
        Assert.Empty(recorded);

        await test.TickAsync();
        listener.RecordObservableInstruments();

        Assert.Equal(OverflowMetrics.MeterName, test.Metrics.Meter.Name);
        Assert.Contains(("reelshelf.overflow.tasks_started", 3L), recorded);
        Assert.Contains(("reelshelf.overflow.tasks_running", 4L), recorded);
    }

    [Fact]
    public async Task SeveralPools_FillTheFirstUpToItsCap_ThenTheNext()
    {
        Harness test = new(o =>
        {
            o.MaxTasks = 10;
            o.MaxStartPerTick = 10;
            o.Ecs.MaxTasks = 4;
            o.Batch.MaxTasks = 6;
        }, pools: ["ecs", "batch"]);
        test.Backlog.Next = new OverflowBacklog(7, Old);
        test.Launchers["ecs"].Running = 1;

        OverflowTick tick = await test.TickAsync();

        Assert.Equal(OverflowTickOutcome.Started, tick.Outcome);
        Assert.Equal(6, tick.Started);
        Assert.Equal([3], test.Launchers["ecs"].StartRequests);
        Assert.Equal([3], test.Launchers["batch"].StartRequests);
        Assert.Equal(new Dictionary<string, int> { ["ecs"] = 3, ["batch"] = 3 }, tick.StartedByPool);
        Assert.Equal(4, test.Metrics.RunningIn("ecs"));
        Assert.Equal(3, test.Metrics.RunningIn("batch"));
        Assert.Equal(7, test.Metrics.Running);
    }

    [Fact]
    public async Task ThreePools_FillInOrder_Ec2ThenFargateThenBatch()
    {
        Harness test = new(o =>
        {
            o.MaxTasks = 12;
            o.MaxStartPerTick = 12;
            o.Ec2.MaxTasks = 4;
            o.Ecs.MaxTasks = 4;
            o.Batch.MaxTasks = 6;
        }, pools: ["ec2", "ecs", "batch"]);
        test.Backlog.Next = new OverflowBacklog(11, Old);
        test.Launchers["ec2"].Running = 2;
        // EC2 Spot can only place one more this tick.
        test.Launchers["ec2"].Accept = 1;

        OverflowTick tick = await test.TickAsync();

        Assert.Equal(OverflowTickOutcome.Started, tick.Outcome);
        Assert.Equal(9, tick.Started);
        Assert.Equal([2], test.Launchers["ec2"].StartRequests);
        Assert.Equal([4], test.Launchers["ecs"].StartRequests);
        Assert.Equal([4], test.Launchers["batch"].StartRequests);
        Assert.Equal(new Dictionary<string, int> { ["ec2"] = 1, ["ecs"] = 4, ["batch"] = 4 }, tick.StartedByPool);
        Assert.Equal(3, test.Metrics.RunningIn("ec2"));
    }

    [Fact]
    public async Task APoolThatOnlyStartsSome_HandsTheRestToTheNext()
    {
        Harness test = new(o =>
        {
            o.MaxTasks = 10;
            o.MaxStartPerTick = 10;
            o.Ecs.MaxTasks = 4;
            o.Batch.MaxTasks = 6;
        }, pools: ["ecs", "batch"]);
        test.Backlog.Next = new OverflowBacklog(5, Old);
        // No Fargate Spot capacity for all of them.
        test.Launchers["ecs"].Accept = 1;

        OverflowTick tick = await test.TickAsync();

        Assert.Equal(5, tick.Started);
        Assert.Equal([4], test.Launchers["ecs"].StartRequests);
        Assert.Equal([4], test.Launchers["batch"].StartRequests);
    }

    [Fact]
    public async Task APoolThatThrows_HandsItsShareToTheNext()
    {
        Harness test = new(o =>
        {
            o.MaxTasks = 10;
            o.MaxStartPerTick = 10;
            o.Ecs.MaxTasks = 4;
        }, pools: ["ecs", "batch"]);
        test.Backlog.Next = new OverflowBacklog(3, Old);
        test.Launchers["ecs"].StartError = new InvalidOperationException("AccessDenied");

        OverflowTick tick = await test.TickAsync();

        Assert.Equal(OverflowTickOutcome.Started, tick.Outcome);
        Assert.Equal(3, tick.Started);
        Assert.Equal([3], test.Launchers["batch"].StartRequests);
        Assert.Equal(new Dictionary<string, int> { ["batch"] = 3 }, tick.StartedByPool);
    }

    [Fact]
    public async Task EveryPoolThrowing_IsAStartFailure()
    {
        Harness test = new(o => o.MaxTasks = 10, pools: ["ecs", "batch"]);
        test.Backlog.Next = new OverflowBacklog(3, Old);
        test.Launchers["ecs"].StartError = new InvalidOperationException("AccessDenied");
        test.Launchers["batch"].StartError = new InvalidOperationException("PERMISSION_DENIED");

        Assert.Equal(OverflowTickOutcome.StartFailed, (await test.TickAsync()).Outcome);
    }

    [Fact]
    public async Task APoolThatCantBeCounted_IsTakenAsFull()
    {
        Harness test = new(o =>
        {
            o.MaxTasks = 8;
            o.MaxStartPerTick = 10;
            o.Ecs.MaxTasks = 4;
            o.Batch.MaxTasks = 6;
        }, pools: ["ecs", "batch"]);
        test.Backlog.Next = new OverflowBacklog(20, Old);
        test.Launchers["ecs"].ListError = new InvalidOperationException("throttled");

        OverflowTick tick = await test.TickAsync();

        // ECS may have its 4 up, so only 8 - 4 = 4 go to Batch, and none to ECS.
        Assert.Equal(OverflowTickOutcome.Started, tick.Outcome);
        Assert.Equal(4, tick.Running);
        Assert.Empty(test.Launchers["ecs"].StartRequests);
        Assert.Equal([4], test.Launchers["batch"].StartRequests);
        Assert.Null(test.Metrics.RunningIn("ecs"));
    }

    [Fact]
    public async Task EveryPoolAtItsOwnCap_StartsNothing_EvenUnderTheOverallCap()
    {
        Harness test = new(o =>
        {
            o.MaxTasks = 20;
            o.MaxStartPerTick = 10;
            o.Ecs.MaxTasks = 4;
            o.Batch.MaxTasks = 6;
        }, pools: ["ecs", "batch"]);
        test.Backlog.Next = new OverflowBacklog(50, Old);
        test.Launchers["ecs"].Running = 4;
        test.Launchers["batch"].Running = 6;

        Assert.Equal(OverflowTickOutcome.Enough, (await test.TickAsync()).Outcome);
        Assert.Empty(test.Launchers["ecs"].StartRequests);
        Assert.Empty(test.Launchers["batch"].StartRequests);
    }

    [Fact]
    public async Task TheOverallCap_LimitsEveryPoolTogether()
    {
        Harness test = new(o =>
        {
            o.MaxTasks = 5;
            o.MaxStartPerTick = 10;
            o.Ecs.MaxTasks = 4;
            o.Batch.MaxTasks = 6;
        }, pools: ["ecs", "batch"]);
        test.Backlog.Next = new OverflowBacklog(50, Old);
        test.Launchers["batch"].Running = 2;

        OverflowTick tick = await test.TickAsync();

        Assert.Equal(3, tick.Started);
        Assert.Equal([3], test.Launchers["ecs"].StartRequests);
        Assert.Empty(test.Launchers["batch"].StartRequests);
    }

    [Fact]
    public void APoolsCap_DefaultsToTheOverallOne()
    {
        OverflowOptions options = new() { MaxTasks = 7, Ecs = { MaxTasks = 4 }, Ec2 = { MaxTasks = 5 } };
        Assert.Equal(5, options.MaxTasksFor("ec2"));
        Assert.Equal(4, options.MaxTasksFor("ecs"));
        Assert.Equal(7, options.MaxTasksFor("batch"));
    }

    [Fact]
    public async Task Metrics_AreTaggedWithThePool()
    {
        Harness test = new(o =>
        {
            o.MaxTasks = 10;
            o.Ecs.MaxTasks = 1;
        }, pools: ["ecs", "batch"]);
        test.Backlog.Next = new OverflowBacklog(3, Old);
        List<(string Name, long Value, string? Pool)> recorded = [];
        using MeterListener listener = new();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter == test.Metrics.Meter)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            string? pool = null;
            foreach (KeyValuePair<string, object?> tag in tags)
            {
                if (tag.Key == OverflowMetrics.PoolTag)
                {
                    pool = tag.Value as string;
                }
            }

            recorded.Add((instrument.Name, value, pool));
        });
        listener.Start();

        await test.TickAsync();
        listener.RecordObservableInstruments();

        Assert.Contains(("reelshelf.overflow.tasks_started", 1L, "ecs"), recorded);
        Assert.Contains(("reelshelf.overflow.tasks_started", 2L, "batch"), recorded);
        Assert.Contains(("reelshelf.overflow.tasks_running", 1L, "ecs"), recorded);
        Assert.Contains(("reelshelf.overflow.tasks_running", 2L, "batch"), recorded);
    }

    [Fact]
    public void Setup_IsOffByDefault()
    {
        Assert.False(RegistersTheService(Build(new Dictionary<string, string?>())));
    }

    [Fact]
    public void Setup_Enabled_RegistersTheService()
    {
        IServiceCollection services = Build(CompleteSettings());
        Assert.True(RegistersTheService(services));
        using ServiceProvider provider = services.BuildServiceProvider();
        OverflowOptions options = provider.GetRequiredService<IOptions<OverflowOptions>>().Value;
        Assert.Equal("europe-west9", options.Batch.Region);
    }

    [Fact]
    public void Setup_BothPools_RegistersThemInOrder()
    {
        Dictionary<string, string?> settings = CompleteSettings();
        foreach ((string key, string? value) in EcsSettings())
        {
            settings[key] = value;
        }

        settings["Overflow:Pools"] = "ECS, batch";
        IServiceCollection services = Build(settings);
        services.AddSingleton(TimeProvider.System);
        // No clients: building them needs real credentials.
        services.AddSingleton<IBatchJobs>(new NoBatch());
        services.AddKeyedSingleton<IEcsTasks>("ecs", new NoEcs());
        using ServiceProvider provider = services.BuildServiceProvider();

        OverflowPools pools = provider.GetRequiredService<OverflowPools>();
        Assert.Equal(["ecs", "batch"], pools.All.Select(pool => pool.Name));
        Assert.IsType<EcsOverflowTaskLauncher>(pools.All[0].Launcher);
        Assert.IsType<BatchOverflowTaskLauncher>(pools.All[1].Launcher);
    }

    [Fact]
    public void Setup_ThreePools_RegistersThemInOrder_EachEcsPoolWithItsOwnClient()
    {
        Dictionary<string, string?> settings = CompleteSettings();
        foreach ((string key, string? value) in EcsSettings().Concat(Ec2Settings()))
        {
            settings[key] = value;
        }

        settings["Overflow:Pools"] = "ec2,ecs,batch";
        IServiceCollection services = Build(settings);
        services.AddSingleton(TimeProvider.System);
        NoEcs ec2Client = new();
        NoEcs ecsClient = new();
        services.AddSingleton<IBatchJobs>(new NoBatch());
        services.AddKeyedSingleton<IEcsTasks>("ec2", ec2Client);
        services.AddKeyedSingleton<IEcsTasks>("ecs", ecsClient);
        using ServiceProvider provider = services.BuildServiceProvider();

        OverflowPools pools = provider.GetRequiredService<OverflowPools>();
        Assert.Equal(["ec2", "ecs", "batch"], pools.All.Select(pool => pool.Name));
        Assert.Equal("ec2", Assert.IsType<EcsOverflowTaskLauncher>(pools.All[0].Launcher).Pool);
        Assert.Equal("ecs", Assert.IsType<EcsOverflowTaskLauncher>(pools.All[1].Launcher).Pool);
        Assert.NotSame(pools.All[0].Launcher, pools.All[1].Launcher);
        Assert.Same(ec2Client, provider.GetRequiredKeyedService<IEcsTasks>("ec2"));
        Assert.Same(ecsClient, provider.GetRequiredKeyedService<IEcsTasks>("ecs"));
        Assert.IsType<BatchOverflowTaskLauncher>(pools.All[2].Launcher);
        OverflowOptions options = provider.GetRequiredService<IOptions<OverflowOptions>>().Value;
        Assert.Equal("reelshelf-overflow-ec2-spot", options.Ec2.CapacityProvider);
        // The Fargate pool keeps its default when no capacity provider is set.
        Assert.Equal("FARGATE_SPOT", options.Ecs.CapacityProvider);
    }

    [Fact]
    public void Setup_Ec2Alone_NeedsNoSubnetsOrOtherPoolsSettings()
    {
        Dictionary<string, string?> settings = Ec2Settings();
        settings["Overflow:Enabled"] = "true";
        settings["Overflow:Pools"] = "ec2";

        IServiceCollection services = Build(settings);
        Assert.True(RegistersTheService(services));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IBatchJobs));
        Assert.DoesNotContain(services, descriptor => Equals(descriptor.ServiceKey, "ecs"));
        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.Equal(["ec2"], provider.GetRequiredService<OverflowPools>().All.Select(pool => pool.Name));
    }

    [Theory]
    [InlineData("Overflow:Ec2:Region")]
    [InlineData("Overflow:Ec2:Cluster")]
    [InlineData("Overflow:Ec2:CapacityProvider")]
    [InlineData("Overflow:Ec2:TaskDefinition")]
    public void Setup_Ec2_FailsWithoutARequiredSetting(string key)
    {
        Dictionary<string, string?> settings = Ec2Settings();
        settings["Overflow:Enabled"] = "true";
        settings["Overflow:Pools"] = "ec2";
        settings[key] = " ";

        Assert.Contains($"{key} is missing", Assert.Throws<InvalidOperationException>(() => Build(settings)).Message);
    }

    [Fact]
    public void Setup_Ec2_HasNoDefaultCapacityProvider()
    {
        Dictionary<string, string?> settings = Ec2Settings();
        settings.Remove("Overflow:Ec2:CapacityProvider");
        settings["Overflow:Enabled"] = "true";
        settings["Overflow:Pools"] = "ec2";

        Assert.Contains("Overflow:Ec2:CapacityProvider is missing",
            Assert.Throws<InvalidOperationException>(() => Build(settings)).Message);
    }

    [Theory]
    [InlineData("Overflow:Ec2:TaskDefinition", "reelshelf-worker-overflow-ec2-prod:3", "Overflow:Ec2:TaskDefinition must be the family")]
    [InlineData("Overflow:Ec2:WorkerImage", "ghcr.io/pluscosmic/reelshelf-worker", "Overflow:Ec2:WorkerImage must name a tag")]
    [InlineData("Overflow:Ec2:AccessKeyId", "AKIAEXAMPLE", "Overflow:Ec2:AccessKeyId and SecretAccessKey must both be set")]
    [InlineData("Overflow:Ec2:MaxTasks", "-1", "can't be negative")]
    [InlineData("Overflow:Ec2:Cluster", "reelshelf-overflow-prod", "must be different clusters")]
    public void Setup_Ec2_FailsOnBadSettings(string key, string value, string message)
    {
        Dictionary<string, string?> settings = EcsSettings();
        foreach ((string ec2Key, string? ec2Value) in Ec2Settings())
        {
            settings[ec2Key] = ec2Value;
        }

        settings["Overflow:Enabled"] = "true";
        settings["Overflow:Pools"] = "ec2,ecs";
        settings[key] = value;

        Assert.Contains(message, Assert.Throws<InvalidOperationException>(() => Build(settings)).Message);
    }

    [Fact]
    public void Setup_EcsPoolsNotInUse_AreNotValidated()
    {
        // An incomplete Ec2 section doesn't stop a deployment that only uses the Fargate pool.
        Dictionary<string, string?> settings = EcsSettings();
        settings["Overflow:Enabled"] = "true";
        settings["Overflow:Pools"] = "ecs";
        settings["Overflow:Ec2:Cluster"] = "reelshelf-overflow-ec2-prod";

        Assert.True(RegistersTheService(Build(settings)));
    }

    [Fact]
    public void Setup_EcsAlone_NeedsNoBatchSettings()
    {
        Dictionary<string, string?> settings = EcsSettings();
        settings["Overflow:Enabled"] = "true";
        settings["Overflow:Pools"] = "ecs";

        IServiceCollection services = Build(settings);
        Assert.True(RegistersTheService(services));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IBatchJobs));
        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.Equal(["ecs"], provider.GetRequiredService<OverflowPools>().All.Select(pool => pool.Name));
    }

    [Theory]
    [InlineData("Overflow:Ecs:Region")]
    [InlineData("Overflow:Ecs:Cluster")]
    [InlineData("Overflow:Ecs:TaskDefinition")]
    [InlineData("Overflow:Ecs:Subnets")]
    [InlineData("Overflow:Ecs:SecurityGroups")]
    [InlineData("Overflow:Ecs:CapacityProvider")]
    public void Setup_Ecs_FailsWithoutARequiredSetting(string key)
    {
        Dictionary<string, string?> settings = EcsSettings();
        settings["Overflow:Enabled"] = "true";
        settings["Overflow:Pools"] = "ecs";
        settings[key] = " ";

        Assert.Contains($"{key} is missing", Assert.Throws<InvalidOperationException>(() => Build(settings)).Message);
    }

    [Theory]
    [InlineData("Overflow:Pools", "ecs,lambda", "unknown pools: lambda")]
    [InlineData("Overflow:Pools", " , ", "names no pool")]
    [InlineData("Overflow:Ecs:TaskDefinition", "reelshelf-worker-overflow-prod:3", "must be the family")]
    [InlineData("Overflow:Ecs:WorkerImage", "ghcr.io/pluscosmic/reelshelf-worker", "must name a tag")]
    [InlineData("Overflow:Ecs:AccessKeyId", "AKIAEXAMPLE", "must both be set")]
    [InlineData("Overflow:Ecs:MaxTasks", "-1", "can't be negative")]
    public void Setup_Ecs_FailsOnBadSettings(string key, string value, string message)
    {
        Dictionary<string, string?> settings = EcsSettings();
        settings["Overflow:Enabled"] = "true";
        settings["Overflow:Pools"] = "ecs";
        settings[key] = value;

        Assert.Contains(message, Assert.Throws<InvalidOperationException>(() => Build(settings)).Message);
    }

    [Theory]
    [InlineData("Overflow:JobTypes", "transcode,encode", "unknown job types: encode")]
    [InlineData("Overflow:MaxTasks", "0", "MaxTasks must be at least 1")]
    [InlineData("Overflow:MaxStartPerTick", "0", "MaxStartPerTick must be at least 1")]
    public void Setup_Enabled_FailsOnBadSettings(string key, string value, string message)
    {
        Dictionary<string, string?> settings = CompleteSettings();
        settings[key] = value;

        Assert.Contains(message, Assert.Throws<InvalidOperationException>(() => Build(settings)).Message);
    }

    [Fact]
    public void Setup_InOpenApi_RegistersNothing_EvenWhenEnabledWithoutSettings()
    {
        Assert.False(RegistersTheService(Build(new Dictionary<string, string?> { ["Overflow:Enabled"] = "true" },
            "OpenApi")));
    }

    private static Dictionary<string, string?> CompleteSettings() => new()
    {
        ["Overflow:Enabled"] = "true",
        ["Overflow:Batch:ProjectId"] = "p",
        ["Overflow:Batch:Region"] = "europe-west9",
        ["Overflow:Batch:InstanceTemplate"] = "reelshelf-overflow-c4-v1",
        ["Overflow:Batch:ServiceAccount"] = "worker@p.iam.gserviceaccount.com",
        ["Overflow:Batch:WorkerImage"] = "ghcr.io/pluscosmic/reelshelf-worker:sha-abc",
        ["Overflow:Batch:WorkerSecret"] = "projects/p/secrets/reelshelf-worker-gcp/versions/latest",
        ["Overflow:Batch:ApiBaseUrl"] = "https://reelshelf.example"
    };

    private static Dictionary<string, string?> EcsSettings() => new()
    {
        ["Overflow:Ecs:Region"] = "eu-north-1",
        ["Overflow:Ecs:Cluster"] = "reelshelf-overflow-prod",
        ["Overflow:Ecs:TaskDefinition"] = "reelshelf-worker-overflow-prod",
        ["Overflow:Ecs:Subnets"] = "subnet-a,subnet-b",
        ["Overflow:Ecs:SecurityGroups"] = "sg-1",
        ["Overflow:Ecs:WorkerImage"] = "ghcr.io/pluscosmic/reelshelf-worker:sha-abc"
    };

    private static Dictionary<string, string?> Ec2Settings() => new()
    {
        ["Overflow:Ec2:Region"] = "eu-north-1",
        ["Overflow:Ec2:Cluster"] = "reelshelf-overflow-ec2-prod",
        ["Overflow:Ec2:CapacityProvider"] = "reelshelf-overflow-ec2-spot",
        ["Overflow:Ec2:TaskDefinition"] = "reelshelf-worker-overflow-ec2-prod",
        ["Overflow:Ec2:WorkerImage"] = "ghcr.io/pluscosmic/reelshelf-worker:sha-abc"
    };

    private static bool RegistersTheService(IServiceCollection services) =>
        services.Any(descriptor => descriptor.ServiceType == typeof(IHostedService) &&
                                   descriptor.ImplementationType == typeof(OverflowBackgroundService));

    private static IServiceCollection Build(Dictionary<string, string?> settings, string? environment = null)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = environment ?? Environments.Production });
        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddTranscodeOverflow();
        return builder.Services;
    }

    private sealed class Harness
    {
        public Harness(
            Action<OverflowOptions>? configure = null,
            bool storageConfigured = true,
            TranscodingOptions? transcoding = null,
            string[]? pools = null)
        {
            OverflowOptions options = new() { Enabled = true };
            configure?.Invoke(options);
            ServiceProvider services = new ServiceCollection().AddMetrics().AddSingleton<OverflowMetrics>()
                .BuildServiceProvider();
            Metrics = services.GetRequiredService<OverflowMetrics>();
            foreach (string pool in pools ?? [OverflowOptions.BatchPool])
            {
                Launchers[pool] = new FakeLauncher();
            }

            Scaler = new OverflowScaler(Backlog,
                new OverflowPools(Launchers.Select(pair => new OverflowPool(pair.Key, pair.Value)).ToList()),
                new FakeStorage(storageConfigured), Metrics,
                Options.Create(options), new FixedOptions<TranscodingOptions>(transcoding ?? new TranscodingOptions()),
                Clock, NullLogger<OverflowScaler>.Instance);
        }

        public FakeBacklog Backlog { get; } = new();

        /// <summary>Each pool's fake, in the pools' order.</summary>
        public Dictionary<string, FakeLauncher> Launchers { get; } = [];

        /// <summary>The first pool's fake.</summary>
        public FakeLauncher Launcher => Launchers.Values.First();
        public Clock Clock { get; } = new();
        public OverflowMetrics Metrics { get; }
        public OverflowScaler Scaler { get; }

        public Task<OverflowTick> TickAsync() => Scaler.TickAsync(CancellationToken.None);
    }

    private sealed class FakeBacklog : IOverflowBacklogReader
    {
        public OverflowBacklog Next { get; set; } = OverflowBacklog.Empty;
        public Exception? Error { get; set; }
        public List<(int MaxAttempts, IReadOnlyList<string> JobTypes)> Requests { get; } = [];

        public Task<OverflowBacklog> ReadAsync(int maxAttempts, IReadOnlyList<string> jobTypes,
            CancellationToken cancellationToken)
        {
            Requests.Add((maxAttempts, jobTypes));
            return Error is null ? Task.FromResult(Next) : Task.FromException<OverflowBacklog>(Error);
        }
    }

    private sealed class FakeLauncher : IOverflowTaskLauncher
    {
        public int Running { get; set; }
        public int Accept { get; set; } = int.MaxValue;
        public Exception? ListError { get; set; }
        public Exception? StartError { get; set; }
        public int ListCalls { get; private set; }
        public List<int> StartRequests { get; } = [];

        public Task<int> CountActiveTasksAsync(CancellationToken cancellationToken)
        {
            ListCalls++;
            return ListError is null ? Task.FromResult(Running) : Task.FromException<int>(ListError);
        }

        public Task<int> StartTasksAsync(int count, CancellationToken cancellationToken)
        {
            if (StartError is not null)
            {
                return Task.FromException<int>(StartError);
            }

            StartRequests.Add(count);
            return Task.FromResult(Math.Min(count, Accept));
        }
    }

    private sealed class NoBatch : IBatchJobs
    {
        public IAsyncEnumerable<Google.Cloud.Batch.V1.Job> ListAsync(string parent, string filter,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task CreateAsync(Google.Cloud.Batch.V1.CreateJobRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string name, string reason, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class NoEcs : IEcsTasks
    {
        public Task<IReadOnlyList<string>> ListTaskArnsAsync(string cluster, string startedBy,
            Amazon.ECS.DesiredStatus desiredStatus, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Amazon.ECS.Model.TaskDefinition> DescribeLatestAsync(string family,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Amazon.ECS.Model.TaskDefinition> RegisterAsync(
            Amazon.ECS.Model.RegisterTaskDefinitionRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Amazon.ECS.Model.RunTaskResponse> RunTaskAsync(Amazon.ECS.Model.RunTaskRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeStorage(bool configured) : ITranscodeStorage
    {
        public bool IsConfigured => configured;

        public TranscodeJobUrls UrlsFor(Guid clipId, string jobType, int videoVersion, int currentVersion,
            TimeSpan lifetime) => TranscodeJobUrls.None;

        public Task DeleteSourceAsync(Guid clipId) => Task.CompletedTask;

        public Task DeleteVersionAsync(Guid clipId, int videoVersion) => Task.CompletedTask;

        public Task DeleteOtherVersionsAsync(Guid clipId, int keepVersion) => Task.CompletedTask;

        public Task DeleteAllVersionsAsync(Guid clipId) => Task.CompletedTask;
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class FixedOptions<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
