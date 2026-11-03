using Microsoft.Extensions.Options;

namespace Reelshelf.Video.Transcoding.Overflow;

/// <summary>What one tick saw and did.</summary>
/// <param name="Backlog">The claimable jobs, or null when the tick stopped before counting them.</param>
/// <param name="Running">
/// The overflow tasks running or provisioning in every pool, or null when none could be counted. A pool that couldn't
/// be counted is taken as full.
/// </param>
/// <param name="Started">The tasks this tick started, in every pool.</param>
/// <param name="StartedByPool">The tasks this tick started in each pool it asked.</param>
public sealed record OverflowTick(OverflowTickOutcome Outcome, OverflowBacklog? Backlog = null, int? Running = null,
    int Started = 0, IReadOnlyDictionary<string, int>? StartedByPool = null);

public enum OverflowTickOutcome
{
    /// <summary><see cref="OverflowOptions.Enabled"/> is off.</summary>
    Disabled,

    /// <summary>R2 isn't configured, so there is nothing a worker could do.</summary>
    NoStorage,

    /// <summary>Counting the backlog, or the tasks of every pool, failed; the next tick tries again.</summary>
    CountFailed,

    /// <summary>No job has waited <see cref="OverflowOptions.ScaleUpAfter"/>.</summary>
    BelowThreshold,

    /// <summary>Tasks were started less than <see cref="OverflowOptions.Cooldown"/> ago.</summary>
    CoolingDown,

    /// <summary>
    /// The running tasks already cover the backlog, <see cref="OverflowOptions.MaxTasks"/> are running, or every pool
    /// is at its own cap.
    /// </summary>
    Enough,

    /// <summary>
    /// Starting workers failed in every pool asked (<c>RunTask</c> or <c>CreateJob</c> threw) and none started; the
    /// next tick tries again.
    /// </summary>
    StartFailed,

    /// <summary>Tasks were asked for; <see cref="OverflowTick.Started"/> says how many the backend started.</summary>
    Started
}

/// <summary>
/// The overflow pool's decision (docs/video-pipeline/workers-and-jobs.md#the-overflow-pool). Each tick counts the
/// claimable backlog and the overflow tasks already running or provisioning in every pool, and once the oldest
/// claimable job has waited <see cref="OverflowOptions.ScaleUpAfter"/> starts
/// <c>min(pending - running, MaxTasks - running, MaxStartPerTick)</c> tasks, then none for
/// <see cref="OverflowOptions.Cooldown"/>. They go to the pools in <see cref="OverflowOptions.Pools"/>'s order: each
/// takes as many as its own cap (<see cref="OverflowOptions.MaxTasksFor"/>) leaves room for, and whatever a pool
/// doesn't start (it threw, or had no capacity) goes to the next. A pool that can't be counted is taken as full, so it
/// is skipped and still counts against <see cref="OverflowOptions.MaxTasks"/>. It never stops a task: the workers exit
/// once idle. Errors are logged and the next tick tries again; nothing here throws.
/// </summary>
public sealed class OverflowScaler(
    IOverflowBacklogReader backlogReader,
    OverflowPools pools,
    ITranscodeStorage storage,
    OverflowMetrics metrics,
    IOptions<OverflowOptions> options,
    IOptionsMonitor<TranscodingOptions> transcodingOptions,
    TimeProvider timeProvider,
    ILogger<OverflowScaler> logger)
{
    private DateTimeOffset? _lastStartedAt;
    private bool _warnedNoStorage;

    public async Task<OverflowTick> TickAsync(CancellationToken cancellationToken)
    {
        OverflowOptions current = options.Value;
        if (!current.Enabled)
        {
            return new OverflowTick(OverflowTickOutcome.Disabled);
        }

        if (!storage.IsConfigured)
        {
            if (!_warnedNoStorage)
            {
                logger.LogWarning("Overflow:Enabled is on, but R2 is not configured; no overflow workers are started");
                _warnedNoStorage = true;
            }

            return new OverflowTick(OverflowTickOutcome.NoStorage);
        }

        OverflowBacklog backlog;
        try
        {
            backlog = await backlogReader.ReadAsync(transcodingOptions.CurrentValue.EffectiveMaxAttempts,
                current.JobTypeList, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Failed to count the transcode backlog for the overflow pool");
            return new OverflowTick(OverflowTickOutcome.CountFailed);
        }

        List<PoolState> states = [];
        foreach (OverflowPool pool in pools.All)
        {
            int cap = current.MaxTasksFor(pool.Name);
            try
            {
                int poolRunning = await pool.Launcher.CountActiveTasksAsync(cancellationToken);
                metrics.SetRunning(pool.Name, poolRunning);
                states.Add(new PoolState(pool, cap, poolRunning));
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                metrics.SetRunning(pool.Name, null);
                logger.LogWarning(ex, "Failed to count the {Pool} overflow tasks; taking the pool as full this tick",
                    pool.Name);
                states.Add(new PoolState(pool, cap, null));
            }
        }

        if (states.All(state => state.Running is null))
        {
            return new OverflowTick(OverflowTickOutcome.CountFailed, backlog);
        }

        // A pool that couldn't be counted may be full: count it as such against the overall cap.
        int running = states.Sum(state => state.Running ?? state.Cap);

        if (backlog.Pending == 0 || backlog.OldestPendingAge < current.ScaleUpAfter)
        {
            return new OverflowTick(OverflowTickOutcome.BelowThreshold, backlog, running);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        if (_lastStartedAt is { } lastStartedAt && now < lastStartedAt + current.Cooldown)
        {
            return new OverflowTick(OverflowTickOutcome.CoolingDown, backlog, running);
        }

        int room = states.Sum(state => state.Room);
        int wanted = Math.Min(TasksToStart(backlog.Pending, running, current), room);
        if (wanted <= 0)
        {
            return new OverflowTick(OverflowTickOutcome.Enough, backlog, running);
        }

        int started = 0;
        int failedPools = 0;
        int askedPools = 0;
        Dictionary<string, int> startedByPool = [];
        foreach (PoolState state in states)
        {
            int ask = Math.Min(wanted - started, state.Room);
            if (ask <= 0)
            {
                continue;
            }

            askedPools++;
            int accepted;
            try
            {
                accepted = Math.Clamp(await state.Pool.Launcher.StartTasksAsync(ask, cancellationToken), 0, ask);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                failedPools++;
                logger.LogWarning(ex, "Failed to start {Count} {Pool} overflow tasks; offering them to the next pool",
                    ask, state.Pool.Name);
                continue;
            }

            startedByPool[state.Pool.Name] = accepted;
            if (accepted > 0)
            {
                metrics.AddStarted(state.Pool.Name, accepted);
                metrics.SetRunning(state.Pool.Name, state.Running + accepted);
            }

            if (accepted < ask)
            {
                logger.LogInformation("The {Pool} pool started {Accepted} of {Asked} overflow tasks; offering the rest to the next pool",
                    state.Pool.Name, accepted, ask);
            }

            started += accepted;
            if (started >= wanted)
            {
                break;
            }
        }

        if (started == 0 && failedPools == askedPools)
        {
            return new OverflowTick(OverflowTickOutcome.StartFailed, backlog, running, 0, startedByPool);
        }

        if (started > 0)
        {
            _lastStartedAt = now;
        }

        logger.LogInformation(
            "Started {Started} of {Wanted} overflow tasks ({ByPool}): {Pending} jobs claimable, the oldest waiting {Age}, {Running} tasks already up",
            started, wanted, string.Join(", ", startedByPool.Select(pair => $"{pair.Key} {pair.Value}")),
            backlog.Pending, backlog.OldestPendingAge, running);
        return new OverflowTick(OverflowTickOutcome.Started, backlog, running, started, startedByPool);
    }

    /// <summary>
    /// <c>min(pending - running, MaxTasks - running, MaxStartPerTick)</c>, never below 0: one task per claimable job
    /// the running tasks don't already cover, within the overall cap and the per-tick limit.
    /// </summary>
    public static int TasksToStart(long pending, int running, OverflowOptions options)
    {
        long wanted = Math.Min(pending - running, Math.Min(options.MaxTasks - running, options.MaxStartPerTick));
        return (int)Math.Max(0, wanted);
    }

    /// <summary>A pool as this tick counted it; <see cref="Running"/> is null when counting failed.</summary>
    private sealed record PoolState(OverflowPool Pool, int Cap, int? Running)
    {
        /// <summary>How many more tasks the pool's own cap allows; none when it couldn't be counted.</summary>
        public int Room => Running is { } running ? Math.Max(0, Cap - running) : 0;
    }
}
