using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Reelshelf.Worker.Test;

/// <summary>
/// <see cref="WorkerLoop.PollAsync"/> with <c>Worker:ExitWhenIdleAfter</c>, on a clock whose waits pass at once, with
/// scripted claims in place of the API.
/// </summary>
public sealed class IdleExitTests : IDisposable
{
    private static readonly TimeSpan Limit = TimeSpan.FromMinutes(5);

    private readonly InstantClock clock = new();
    private readonly List<TimeSpan> claims = [];
    private ServiceProvider? services;

    [Fact]
    public async Task Unset_KeepsPollingWithTheIdleBackoff()
    {
        WorkerLoop loop = Loop(exitWhenIdleAfter: null);
        using CancellationTokenSource stopping = new();

        bool exited = await loop.PollAsync(_ =>
        {
            Claimed();
            if (claims.Count == 100)
            {
                stopping.Cancel();
            }

            return Task.FromResult(false);
        }, stopping.Token);

        Assert.False(exited);
        Assert.Equal(100, claims.Count);
        // Hours of an empty queue, never more than the 60 s ceiling (plus jitter) between claims.
        Assert.True(clock.Elapsed > TimeSpan.FromHours(1), $"Only {clock.Elapsed} passed");
        Assert.All(clock.Delays, delay => Assert.True(delay <= TimeSpan.FromSeconds(72), $"Waited {delay}"));
    }

    [Fact]
    public async Task AnEmptyQueue_ExitsAtTheLimit_WithoutSleepingPastIt()
    {
        WorkerLoop loop = Loop(Limit);

        bool exited = await loop.PollAsync(_ =>
        {
            Claimed();
            return Task.FromResult(false);
        }, CancellationToken.None);

        Assert.True(exited);
        // The last wait is cut short so the final claim is right at the limit, and that claim's emptiness ends it.
        Assert.Equal(Limit, clock.Elapsed);
        Assert.Equal(Limit, claims[^1]);
        Assert.All(clock.Delays, delay => Assert.True(delay <= TimeSpan.FromSeconds(72), $"Waited {delay}"));
    }

    [Fact]
    public async Task AClaim_ResetsTheClock()
    {
        WorkerLoop loop = Loop(Limit);
        TimeSpan jobDone = TimeSpan.Zero;

        bool exited = await loop.PollAsync(_ =>
        {
            Claimed();
            // Four minutes in, a job arrives and takes ten minutes.
            if (jobDone == TimeSpan.Zero && clock.Elapsed >= TimeSpan.FromMinutes(4))
            {
                clock.Advance(TimeSpan.FromMinutes(10));
                jobDone = clock.Elapsed;
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }, CancellationToken.None);

        Assert.True(exited);
        Assert.NotEqual(TimeSpan.Zero, jobDone);
        Assert.Equal(jobDone + Limit, clock.Elapsed);
    }

    [Fact]
    public async Task ApiErrors_DoNotCountAsIdle()
    {
        WorkerLoop loop = Loop(Limit);
        TimeSpan outage = TimeSpan.Zero;

        bool exited = await loop.PollAsync(_ =>
        {
            Claimed();
            clock.MarkErroring(false);
            // Two minutes of an empty queue, then the API is down for half an hour.
            if (clock.Elapsed >= TimeSpan.FromMinutes(2) && clock.Elapsed < TimeSpan.FromMinutes(32))
            {
                TimeSpan before = clock.Elapsed;
                clock.Advance(TimeSpan.FromSeconds(1));
                outage += clock.Elapsed - before;
                clock.MarkErroring(true);
                throw new WorkerApiException("The API answered 503");
            }

            return Task.FromResult(false);
        }, CancellationToken.None);

        Assert.True(exited);
        // The claims that failed and the waits after them are left out, so the worker outlives the outage.
        Assert.True(clock.Elapsed > TimeSpan.FromMinutes(32), $"Exited after {clock.Elapsed}");
        TimeSpan erroring = outage + clock.ErrorWaits;
        Assert.Equal(Limit, clock.Elapsed - erroring);
    }

    [Fact]
    public async Task Shutdown_StopsWithoutExitingAsIdle()
    {
        WorkerLoop loop = Loop(Limit);
        using CancellationTokenSource stopping = new();

        bool exited = await loop.PollAsync(_ =>
        {
            Claimed();
            stopping.Cancel();
            return Task.FromResult(false);
        }, stopping.Token);

        Assert.False(exited);
    }

    [Theory]
    [InlineData("00:00:00", false)]
    [InlineData("-00:01:00", false)]
    [InlineData("00:10:00", true)]
    [InlineData(null, true)]
    public void Settings_RejectAZeroOrNegativeLimit(string? value, bool valid)
    {
        WorkerSettings settings = new()
        {
            ApiBaseUrl = "https://reelshelf.example",
            Secret = FakeWorkerApi.Secret,
            ExitWhenIdleAfter = value is null ? null : TimeSpan.Parse(value)
        };

        Assert.Equal(valid, settings.Problem() is null);
    }

    private void Claimed() => claims.Add(clock.Elapsed);

    private WorkerLoop Loop(TimeSpan? exitWhenIdleAfter)
    {
        Dictionary<string, string?> settings = new()
        {
            ["Worker:ApiBaseUrl"] = "https://reelshelf.example",
            ["Worker:Secret"] = FakeWorkerApi.Secret,
            ["Worker:ExitWhenIdleAfter"] = exitWhenIdleAfter?.ToString("c")
        };
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        ServiceCollection collection = new();
        collection.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Debug));
        collection.AddTranscodeWorker(configuration);
        collection.AddSingleton<TimeProvider>(clock);
        services = collection.BuildServiceProvider();
        return services.GetRequiredService<WorkerLoop>();
    }

    public void Dispose() => services?.Dispose();

    /// <summary>
    /// A clock that moves only when told to or when something waits on it: a timer jumps the clock to its due time
    /// and fires at once. Records each wait, and the total of waits taken while the last claim was failing.
    /// </summary>
    private sealed class InstantClock : TimeProvider
    {
        private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        private readonly object gate = new();
        private DateTimeOffset now = Start;

        public TimeSpan Elapsed
        {
            get
            {
                lock (gate)
                {
                    return now - Start;
                }
            }
        }

        public List<TimeSpan> Delays { get; } = [];

        /// <summary>Waits that started after a claim threw.</summary>
        public TimeSpan ErrorWaits { get; private set; }

        /// <summary>Set while the last claim threw, so its wait is counted in <see cref="ErrorWaits"/>.</summary>
        private bool erroring;

        public void Advance(TimeSpan by)
        {
            lock (gate)
            {
                now += by;
            }
        }

        public override DateTimeOffset GetUtcNow()
        {
            lock (gate)
            {
                return now;
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Delays.Add(dueTime);
            if (erroring)
            {
                ErrorWaits += dueTime;
            }

            Advance(dueTime);
            ThreadPool.QueueUserWorkItem(_ => callback(state));
            return new Fired();
        }

        public void MarkErroring(bool value) => erroring = value;

        private sealed class Fired : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => false;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
