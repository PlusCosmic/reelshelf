using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Reelshelf.QueueMetrics;

/// <summary>
/// The <see cref="MeterName"/> meter's observable gauges, which report the latest samples
/// <see cref="QueueMetricsBackgroundService"/> took, tagged <c>queue</c> and <c>trigger</c>. Observing them never
/// touches the database.
/// </summary>
public sealed class RunQueueMetrics
{
    public const string MeterName = "Reelshelf.Queues";

    public const string QueueTag = "queue";
    public const string TriggerTag = "trigger";

    /// <summary>Each queue's latest samples by trigger. A queue whose last sample failed has none.</summary>
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, RunQueueSample>> latest = new();

    public RunQueueMetrics(IMeterFactory meterFactory)
    {
        Meter = meterFactory.Create(MeterName);
        Gauge("reelshelf.queue.pending", "{run}", "Pending runs a worker can claim now.", sample => sample.Pending);
        Gauge("reelshelf.queue.retrying", "{run}", "Pending runs waiting out the retry delay after a failed attempt.",
            sample => sample.Retrying);
        Meter.CreateObservableGauge("reelshelf.queue.oldest_pending_age",
            () => Observe(sample => sample.OldestPendingAgeSeconds), "s",
            "How long the oldest pending run has been queued; zero when nothing is pending.");
        Gauge("reelshelf.queue.running", "{run}", "Runs being processed, stale ones included.", sample => sample.Running);
        Gauge("reelshelf.queue.stale_running", "{run}",
            "Running runs past the queue's stale-after, whose worker stopped; they will be claimed again.",
            sample => sample.StaleRunning);
        Gauge("reelshelf.queue.succeeded_last_hour", "{run}", "Runs that succeeded in the last hour.",
            sample => sample.SucceededLastHour);
        Gauge("reelshelf.queue.failed_last_hour", "{run}", "Runs that failed for good in the last hour.",
            sample => sample.FailedLastHour);
        Gauge("reelshelf.queue.exhausted_last_hour", "{run}",
            "Runs that failed in the last hour because they used up their attempts.",
            sample => sample.ExhaustedLastHour);
    }

    public Meter Meter { get; }

    /// <summary>
    /// Replaces a queue's samples. A trigger reported before but missing now reports zeros, so its gauges fall to
    /// zero rather than stopping.
    /// </summary>
    public void Publish(string queue, IReadOnlyList<RunQueueSample> samples)
    {
        Dictionary<string, RunQueueSample> next = samples.ToDictionary(sample => sample.Trigger);
        if (latest.TryGetValue(queue, out IReadOnlyDictionary<string, RunQueueSample>? previous))
        {
            foreach (string trigger in previous.Keys)
            {
                next.TryAdd(trigger, RunQueueSample.Empty(trigger));
            }
        }

        latest[queue] = next;
    }

    /// <summary>
    /// Stops reporting a queue whose sample failed, so its gauges go missing instead of showing old numbers. The
    /// triggers it had are forgotten too, which only matters for zero-filling once it reports again.
    /// </summary>
    public void Clear(string queue) => latest.TryRemove(queue, out _);

    private void Gauge(string name, string unit, string description, Func<RunQueueSample, long> value)
    {
        Meter.CreateObservableGauge(name, () => Observe(value), unit, description);
    }

    private IEnumerable<Measurement<T>> Observe<T>(Func<RunQueueSample, T> value) where T : struct
    {
        List<Measurement<T>> measurements = [];
        foreach ((string queue, IReadOnlyDictionary<string, RunQueueSample> samples) in latest)
        {
            foreach (RunQueueSample sample in samples.Values)
            {
                measurements.Add(new Measurement<T>(value(sample),
                    new KeyValuePair<string, object?>(QueueTag, queue),
                    new KeyValuePair<string, object?>(TriggerTag, sample.Trigger)));
            }
        }

        return measurements;
    }
}
