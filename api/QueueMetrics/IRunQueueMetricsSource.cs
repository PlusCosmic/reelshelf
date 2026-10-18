namespace Reelshelf.QueueMetrics;

/// <summary>
/// One queue's numbers for one trigger, as <see cref="RunQueueMetrics"/> reports them. Counts are runs; the age is
/// in seconds and zero when nothing is pending.
/// </summary>
/// <param name="Pending">Pending runs a worker can claim now.</param>
/// <param name="Retrying">Pending runs that failed an attempt and are waiting out the queue's retry delay.</param>
/// <param name="OldestPendingAgeSeconds">How long the oldest pending run (claimable or retrying) has been queued.</param>
/// <param name="Running">Runs marked running, stale ones included.</param>
/// <param name="StaleRunning">Running runs past the queue's stale-after: their worker stopped, and they will be claimed again.</param>
/// <param name="SucceededLastHour">Runs that succeeded in the last hour.</param>
/// <param name="FailedLastHour">Runs that failed for good in the last hour, for any reason.</param>
/// <param name="ExhaustedLastHour">Of those, runs that failed because they used up their attempts.</param>
public sealed record RunQueueSample(
    string Trigger,
    long Pending,
    long Retrying,
    double OldestPendingAgeSeconds,
    long Running,
    long StaleRunning,
    long SucceededLastHour,
    long FailedLastHour,
    long ExhaustedLastHour)
{
    public static RunQueueSample Empty(string trigger) => new(trigger, 0, 0, 0, 0, 0, 0, 0, 0);
}

/// <summary>
/// Samples one queue for <see cref="QueueMetricsBackgroundService"/>. Register one per queue as a singleton; a run
/// table with the usual columns needs only <see cref="QueueMetricsSetup.AddRunTableQueueMetrics"/>.
/// </summary>
public interface IRunQueueMetricsSource
{
    /// <summary>The queue's name, reported as the <c>queue</c> tag.</summary>
    string Queue { get; }

    /// <summary>The queue's current numbers, one sample per trigger.</summary>
    Task<IReadOnlyList<RunQueueSample>> SampleAsync(CancellationToken cancellationToken);
}
