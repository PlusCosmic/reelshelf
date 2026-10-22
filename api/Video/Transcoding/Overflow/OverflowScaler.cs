using Microsoft.Extensions.Options;

namespace Reelshelf.Video.Transcoding.Overflow;

/// <summary>What one tick saw and did.</summary>
/// <param name="Backlog">The claimable jobs, or null when the tick stopped before counting them.</param>
/// <param name="Running">The overflow tasks running or provisioning, or null when they weren't counted.</param>
/// <param name="Started">The tasks this tick started.</param>
public sealed record OverflowTick(OverflowTickOutcome Outcome, OverflowBacklog? Backlog = null, int? Running = null,
    int Started = 0);

public enum OverflowTickOutcome
{
    /// <summary><see cref="OverflowOptions.Enabled"/> is off.</summary>
    Disabled,

    /// <summary>R2 isn't configured, so there is nothing a worker could do.</summary>
    NoStorage,

    /// <summary>Counting the backlog or the tasks failed; the next tick tries again.</summary>
    CountFailed,

    /// <summary>No job has waited <see cref="OverflowOptions.ScaleUpAfter"/>.</summary>
    BelowThreshold,

    /// <summary>Tasks were started less than <see cref="OverflowOptions.Cooldown"/> ago.</summary>
    CoolingDown,

    /// <summary>The running tasks already cover the backlog, or <see cref="OverflowOptions.MaxTasks"/> are running.</summary>
    Enough,

    /// <summary>Starting workers failed (<c>RunTask</c> or <c>CreateJob</c>); the next tick tries again.</summary>
    StartFailed,

    /// <summary>Tasks were asked for; <see cref="OverflowTick.Started"/> says how many the backend started.</summary>
    Started
}

/// <summary>
/// The overflow pool's decision (docs/video-pipeline/workers-and-jobs.md#the-overflow-pool). Each tick counts the
/// claimable backlog and the overflow tasks already running or provisioning, and once the oldest claimable job has
/// waited <see cref="OverflowOptions.ScaleUpAfter"/> starts
/// <c>min(pending - running, MaxTasks - running, MaxStartPerTick)</c> tasks, then none for
/// <see cref="OverflowOptions.Cooldown"/>. It never stops a task: the workers exit once idle. Errors are logged and
/// the next tick tries again; nothing here throws.
/// </summary>
public sealed class OverflowScaler(
    IOverflowBacklogReader backlogReader,
    IOverflowTaskLauncher launcher,
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

        int running;
        try
        {
            running = await launcher.CountActiveTasksAsync(cancellationToken);
            metrics.SetRunning(running);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            metrics.SetRunning(null);
            logger.LogWarning(ex, "Failed to count the overflow tasks");
            return new OverflowTick(OverflowTickOutcome.CountFailed, backlog);
        }

        if (backlog.Pending == 0 || backlog.OldestPendingAge < current.ScaleUpAfter)
        {
            return new OverflowTick(OverflowTickOutcome.BelowThreshold, backlog, running);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        if (_lastStartedAt is { } lastStartedAt && now < lastStartedAt + current.Cooldown)
        {
            return new OverflowTick(OverflowTickOutcome.CoolingDown, backlog, running);
        }

        int wanted = TasksToStart(backlog.Pending, running, current);
        if (wanted <= 0)
        {
            return new OverflowTick(OverflowTickOutcome.Enough, backlog, running);
        }

        int started;
        try
        {
            started = await launcher.StartTasksAsync(wanted, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Failed to start {Count} overflow tasks", wanted);
            return new OverflowTick(OverflowTickOutcome.StartFailed, backlog, running);
        }

        if (started > 0)
        {
            _lastStartedAt = now;
            metrics.AddStarted(started);
            metrics.SetRunning(running + started);
        }

        logger.LogInformation(
            "Started {Started} of {Wanted} overflow tasks: {Pending} jobs claimable, the oldest waiting {Age}, {Running} tasks already up",
            started, wanted, backlog.Pending, backlog.OldestPendingAge, running);
        return new OverflowTick(OverflowTickOutcome.Started, backlog, running, started);
    }

    /// <summary>
    /// <c>min(pending - running, MaxTasks - running, MaxStartPerTick)</c>, never below 0: one task per claimable job
    /// the running tasks don't already cover, within the cap and the per-tick limit.
    /// </summary>
    public static int TasksToStart(long pending, int running, OverflowOptions options)
    {
        long wanted = Math.Min(pending - running, Math.Min(options.MaxTasks - running, options.MaxStartPerTick));
        return (int)Math.Max(0, wanted);
    }
}
