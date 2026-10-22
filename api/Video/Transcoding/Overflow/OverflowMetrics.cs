using System.Diagnostics.Metrics;

namespace Reelshelf.Video.Transcoding.Overflow;

/// <summary>
/// The <see cref="MeterName"/> meter: a gauge of the overflow tasks running or provisioning as the scaler last
/// counted them, and a counter of the tasks it has started. Exported with the queue gauges when
/// <c>QueueMetrics:Exporter=otlp</c>.
/// </summary>
public sealed class OverflowMetrics
{
    public const string MeterName = "Reelshelf.Overflow";

    private readonly Counter<long> _started;

    /// <summary>The last count, or -1 before the first one and after a count failed, so the gauge reports nothing.</summary>
    private long _running = -1;

    public OverflowMetrics(IMeterFactory meterFactory)
    {
        Meter = meterFactory.Create(MeterName);
        Meter.CreateObservableGauge("reelshelf.overflow.tasks_running", ObserveRunning, "{task}",
            "Overflow worker tasks running or provisioning on Batch, as last counted.");
        _started = Meter.CreateCounter<long>("reelshelf.overflow.tasks_started", "{task}",
            "Overflow worker tasks started on Batch.");
    }

    public Meter Meter { get; }

    public long? Running => Interlocked.Read(ref _running) is var running and >= 0 ? running : null;

    public void SetRunning(long? running) => Interlocked.Exchange(ref _running, running ?? -1);

    public void AddStarted(int count)
    {
        if (count > 0)
        {
            _started.Add(count);
        }
    }

    private IEnumerable<Measurement<long>> ObserveRunning() =>
        Running is { } running ? [new Measurement<long>(running)] : [];
}
