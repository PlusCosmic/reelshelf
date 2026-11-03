using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Reelshelf.Video.Transcoding.Overflow;

/// <summary>
/// The <see cref="MeterName"/> meter: a gauge of the overflow tasks running or provisioning as the scaler last
/// counted them, and a counter of the tasks it has started, both tagged <c>pool</c> (<c>ecs</c>, <c>batch</c>).
/// Exported with the queue gauges when <c>QueueMetrics:Exporter=otlp</c>.
/// </summary>
public sealed class OverflowMetrics
{
    public const string MeterName = "Reelshelf.Overflow";

    public const string PoolTag = "pool";

    private readonly Counter<long> _started;

    /// <summary>Each pool's last count; a pool is left out before its first count and after one failed.</summary>
    private readonly ConcurrentDictionary<string, long> _running = new();

    public OverflowMetrics(IMeterFactory meterFactory)
    {
        Meter = meterFactory.Create(MeterName);
        Meter.CreateObservableGauge("reelshelf.overflow.tasks_running", ObserveRunning, "{task}",
            "Overflow worker tasks running or provisioning, per pool, as last counted.");
        _started = Meter.CreateCounter<long>("reelshelf.overflow.tasks_started", "{task}",
            "Overflow worker tasks started, per pool.");
    }

    public Meter Meter { get; }

    /// <summary>The pool's last count, or null before its first and after one failed.</summary>
    public long? RunningIn(string pool) => _running.TryGetValue(pool, out long running) ? running : null;

    /// <summary>Every counted pool's tasks together, or null while none has a count.</summary>
    public long? Running => _running.IsEmpty ? null : _running.Values.Sum();

    public void SetRunning(string pool, long? running)
    {
        if (running is { } value)
        {
            _running[pool] = value;
        }
        else
        {
            _running.TryRemove(pool, out _);
        }
    }

    public void AddStarted(string pool, int count)
    {
        if (count > 0)
        {
            _started.Add(count, new KeyValuePair<string, object?>(PoolTag, pool));
        }
    }

    private IEnumerable<Measurement<long>> ObserveRunning() =>
        _running.Select(pair => new Measurement<long>(pair.Value, new KeyValuePair<string, object?>(PoolTag, pair.Key)));
}
