using System.Diagnostics.Metrics;
using Amazon.ECS;
using CapacityProviderStrategyItem = Amazon.ECS.Model.CapacityProviderStrategyItem;
using RunTaskRequest = Amazon.ECS.Model.RunTaskRequest;
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
/// The overflow pool's decision (<see cref="OverflowScaler"/>) against a fake backlog and a fake ECS, its startup
/// checks (<see cref="OverflowSetup"/>) and the <c>RunTask</c> request it sends.
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
    public async Task ListTasksFailing_IsLoggedAndRetriedOnTheNextTick()
    {
        Harness test = new();
        test.Backlog.Next = new OverflowBacklog(10, Old);
        test.Launcher.ListError = new AmazonECSException("throttled");

        OverflowTick failed = await test.TickAsync();
        Assert.Equal(OverflowTickOutcome.CountFailed, failed.Outcome);
        Assert.Empty(test.Launcher.StartRequests);
        Assert.Null(test.Metrics.Running);

        test.Launcher.ListError = null;
        Assert.Equal(4, (await test.TickAsync()).Started);
    }

    [Fact]
    public async Task RunTaskFailing_IsLoggedAndRetriedOnTheNextTick_WithoutACooldown()
    {
        Harness test = new();
        test.Backlog.Next = new OverflowBacklog(10, Old);
        test.Launcher.StartError = new AmazonECSException("AccessDenied");

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
        Assert.Equal(["subnet-a", "subnet-b"], options.SubnetList);
    }

    [Theory]
    [InlineData("Overflow:Cluster")]
    [InlineData("Overflow:TaskDefinition")]
    [InlineData("Overflow:Subnets")]
    [InlineData("Overflow:SecurityGroups")]
    [InlineData("Overflow:Aws:Region")]
    public void Setup_Enabled_FailsWithoutARequiredSetting(string key)
    {
        Dictionary<string, string?> settings = CompleteSettings();
        settings[key] = " ";

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => Build(settings));
        Assert.Contains($"{key} is missing", error.Message);
    }

    [Theory]
    [InlineData("Overflow:Aws:AccessKeyId", "AKIAEXAMPLE", "AccessKeyId and SecretAccessKey")]
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

    [Fact]
    public void RunTaskRequest_LaunchesOnFargateSpotWithAPublicIp()
    {
        OverflowOptions options = new()
        {
            Cluster = "reelshelf",
            TaskDefinition = "reelshelf-worker:3",
            Subnets = "subnet-a, subnet-b",
            SecurityGroups = "sg-1",
            StartedBy = "reelshelf-overflow"
        };

        RunTaskRequest request = EcsOverflowTaskLauncher.BuildRunTaskRequest(options, 3);

        Assert.Equal("reelshelf", request.Cluster);
        Assert.Equal("reelshelf-worker:3", request.TaskDefinition);
        Assert.Equal(3, request.Count);
        Assert.Equal("reelshelf-overflow", request.StartedBy);
        Assert.Null(request.LaunchType);
        CapacityProviderStrategyItem strategy = Assert.Single(request.CapacityProviderStrategy);
        Assert.Equal("FARGATE_SPOT", strategy.CapacityProvider);
        Assert.Equal(["subnet-a", "subnet-b"], request.NetworkConfiguration.AwsvpcConfiguration.Subnets);
        Assert.Equal(["sg-1"], request.NetworkConfiguration.AwsvpcConfiguration.SecurityGroups);
        Assert.Equal(AssignPublicIp.ENABLED, request.NetworkConfiguration.AwsvpcConfiguration.AssignPublicIp);
        // The task definition's roles, secrets and environment are left alone.
        Assert.Null(request.Overrides);
        // No tags: the API's IAM user has no ecs:TagResource, and startedBy is how the tasks are found again.
        Assert.True(request.Tags is null or { Count: 0 });
        Assert.Null(request.PropagateTags);
        Assert.False(string.IsNullOrEmpty(request.ClientToken));
        Assert.NotEqual(request.ClientToken, EcsOverflowTaskLauncher.BuildRunTaskRequest(options, 3).ClientToken);
    }

    private static Dictionary<string, string?> CompleteSettings() => new()
    {
        ["Overflow:Enabled"] = "true",
        ["Overflow:Cluster"] = "reelshelf",
        ["Overflow:TaskDefinition"] = "reelshelf-worker",
        ["Overflow:Subnets"] = "subnet-a,subnet-b",
        ["Overflow:SecurityGroups"] = "sg-1",
        ["Overflow:Aws:Region"] = "eu-north-1"
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
            TranscodingOptions? transcoding = null)
        {
            OverflowOptions options = new() { Enabled = true };
            configure?.Invoke(options);
            ServiceProvider services = new ServiceCollection().AddMetrics().AddSingleton<OverflowMetrics>()
                .BuildServiceProvider();
            Metrics = services.GetRequiredService<OverflowMetrics>();
            Scaler = new OverflowScaler(Backlog, Launcher, new FakeStorage(storageConfigured), Metrics,
                Options.Create(options), new FixedOptions<TranscodingOptions>(transcoding ?? new TranscodingOptions()),
                Clock, NullLogger<OverflowScaler>.Instance);
        }

        public FakeBacklog Backlog { get; } = new();
        public FakeLauncher Launcher { get; } = new();
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
