using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using OpenTelemetry.Metrics;
using Reelshelf.ApexLegends.LegendDetection;
using Reelshelf.QueueMetrics;
using Xunit;

namespace Reelshelf.Test.QueueMetrics;

/// <summary>The gauges, the sampler that feeds them, and the per-table registration, without a database.</summary>
public class RunQueueMetricsTests
{
    private static readonly string[] Instruments =
    [
        "reelshelf.queue.pending", "reelshelf.queue.retrying", "reelshelf.queue.oldest_pending_age",
        "reelshelf.queue.running", "reelshelf.queue.stale_running", "reelshelf.queue.succeeded_last_hour",
        "reelshelf.queue.failed_last_hour", "reelshelf.queue.exhausted_last_hour"
    ];

    [Fact]
    public void Gauges_ReportTheLatestSamples_TaggedByQueueAndTrigger()
    {
        using ServiceProvider services = new ServiceCollection().AddMetrics().AddSingleton<RunQueueMetrics>()
            .BuildServiceProvider();
        RunQueueMetrics metrics = services.GetRequiredService<RunQueueMetrics>();
        Assert.Equal(RunQueueMetrics.MeterName, metrics.Meter.Name);

        metrics.Publish("legend_detection",
        [
            new RunQueueSample("auto", 5, 1, 120.5, 2, 1, 30, 3, 2),
            RunQueueSample.Empty("manual")
        ]);
        List<Recorded> recorded = Collect(metrics);

        Assert.Equal(Instruments.Order(), recorded.Select(r => r.Instrument).Distinct().Order());
        Assert.Equal(5, Value(recorded, "reelshelf.queue.pending", "legend_detection", "auto"));
        Assert.Equal(1, Value(recorded, "reelshelf.queue.retrying", "legend_detection", "auto"));
        Assert.Equal(120.5, Value(recorded, "reelshelf.queue.oldest_pending_age", "legend_detection", "auto"));
        Assert.Equal(2, Value(recorded, "reelshelf.queue.running", "legend_detection", "auto"));
        Assert.Equal(1, Value(recorded, "reelshelf.queue.stale_running", "legend_detection", "auto"));
        Assert.Equal(30, Value(recorded, "reelshelf.queue.succeeded_last_hour", "legend_detection", "auto"));
        Assert.Equal(3, Value(recorded, "reelshelf.queue.failed_last_hour", "legend_detection", "auto"));
        Assert.Equal(2, Value(recorded, "reelshelf.queue.exhausted_last_hour", "legend_detection", "auto"));
        Assert.Equal(0, Value(recorded, "reelshelf.queue.pending", "legend_detection", "manual"));

        // A trigger that drops out of the sample falls to zero instead of vanishing.
        metrics.Publish("legend_detection", [RunQueueSample.Empty("manual")]);
        Assert.Equal(0, Value(Collect(metrics), "reelshelf.queue.pending", "legend_detection", "auto"));

        // A queue whose sample failed stops reporting.
        metrics.Clear("legend_detection");
        Assert.Empty(Collect(metrics));
    }

    [Fact]
    public async Task Sampler_PublishesEachQueue_AndDropsOneThatFails()
    {
        using ServiceProvider services = new ServiceCollection().AddMetrics().AddSingleton<RunQueueMetrics>()
            .BuildServiceProvider();
        RunQueueMetrics metrics = services.GetRequiredService<RunQueueMetrics>();
        FakeSource healthy = new("clip_summary", [new RunQueueSample("auto", 4, 0, 10, 0, 0, 0, 0, 0)]);
        FakeSource broken = new("clip_transcription", null);
        metrics.Publish("clip_transcription", [new RunQueueSample("auto", 9, 0, 0, 0, 0, 0, 0, 0)]);
        QueueMetricsBackgroundService sampler = new([healthy, broken], metrics,
            Options.Create(new QueueMetricsOptions()), NullLogger<QueueMetricsBackgroundService>.Instance);

        await sampler.SampleAsync([healthy, broken], CancellationToken.None);

        List<Recorded> recorded = Collect(metrics);
        Assert.Equal(4, Value(recorded, "reelshelf.queue.pending", "clip_summary", "auto"));
        Assert.DoesNotContain(recorded, r => (string?)r.Queue == "clip_transcription");
    }

    [Fact]
    public void Registration_AddsOneSourcePerRunTable_ReadingTheAttemptLimitFromOptions()
    {
        ServiceCollection collection = new();
        collection.AddSingleton(NpgsqlDataSource.Create("Host=localhost;Database=unused"));
        collection.Configure<LegendDetectionOptions>(options => options.MaxAttempts = 5);
        collection.AddRunTableQueueMetrics<LegendDetectionOptions>(QueueMetricsSetup.LegendDetectionQueue,
            options => options.MaxAttempts);
        using ServiceProvider services = collection.BuildServiceProvider();

        IRunQueueMetricsSource source = Assert.Single(services.GetServices<IRunQueueMetricsSource>());
        Assert.Equal("legend_detection", source.Queue);
    }

    [Fact]
    public void Sql_FiltersBothQueries_AndMatchesThePartialIndexes()
    {
        string sql = RunTableMetricsSource.BuildSql(QueueMetricsSetup.LegendDetectionQueue);
        Assert.Contains("WHERE status IN ('pending', 'running') AND (archived_at IS NULL)", sql);
        Assert.Contains("WHERE status IN ('succeeded', 'failed') AND completed_at >= now() - @Window AND (archived_at IS NULL)", sql);
        Assert.DoesNotContain("archived_at", RunTableMetricsSource.BuildSql(QueueMetricsSetup.ClipSummaryQueue));
        Assert.Throws<ArgumentException>(() => new RunTableMetricsSource(
            QueueMetricsSetup.ClipSummaryQueue with { Table = "clip_summary_run; DROP TABLE clip" },
            NpgsqlDataSource.Create("Host=localhost"), () => 3));
    }

    [Fact]
    public void Setup_AddsTheOtlpExporterOnlyWhenAsked_AndRejectsAnUnknownOne()
    {
        using (ServiceProvider none = Build(new Dictionary<string, string?>()))
        {
            Assert.Null(none.GetService<MeterProvider>());
        }

        using ServiceProvider otlp = Build(new Dictionary<string, string?>
        {
            ["QueueMetrics:Exporter"] = "otlp",
            ["QueueMetrics:Otlp:Endpoint"] = "http://localhost:4318/v1/metrics",
            ["QueueMetrics:Otlp:Protocol"] = "http/protobuf"
        });
        Assert.NotNull(otlp.GetService<MeterProvider>());
        Assert.Equal(["clip_summary", "clip_transcription", "legend_detection", "transcode"],
            otlp.GetServices<IRunQueueMetricsSource>().Select(source => source.Queue).Order());
        Assert.Contains(otlp.GetServices<IHostedService>(), service => service is QueueMetricsBackgroundService);

        Assert.Throws<InvalidOperationException>(() =>
            Build(new Dictionary<string, string?> { ["QueueMetrics:Exporter"] = "statsd" }));
        using ServiceProvider disabled = Build(new Dictionary<string, string?> { ["QueueMetrics:Enabled"] = "false" });
        Assert.DoesNotContain(disabled.GetServices<IHostedService>(), service => service is QueueMetricsBackgroundService);
    }

    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddSingleton(NpgsqlDataSource.Create("Host=localhost;Database=unused"));
        builder.AddQueueMetrics();
        return builder.Services.BuildServiceProvider();
    }

    private static double Value(List<Recorded> recorded, string instrument, string queue, string trigger)
    {
        return Assert.Single(recorded,
            r => r.Instrument == instrument && (string?)r.Queue == queue && (string?)r.Trigger == trigger).Value;
    }

    private static List<Recorded> Collect(RunQueueMetrics metrics)
    {
        List<Recorded> recorded = [];
        using MeterListener listener = new();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter == metrics.Meter)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            recorded.Add(Recorded.From(instrument, value, tags)));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            recorded.Add(Recorded.From(instrument, value, tags)));
        listener.Start();
        listener.RecordObservableInstruments();
        return recorded;
    }

    private sealed record Recorded(string Instrument, double Value, object? Queue, object? Trigger)
    {
        public static Recorded From(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            object? queue = null;
            object? trigger = null;
            foreach (KeyValuePair<string, object?> tag in tags)
            {
                if (tag.Key == RunQueueMetrics.QueueTag)
                {
                    queue = tag.Value;
                }
                else if (tag.Key == RunQueueMetrics.TriggerTag)
                {
                    trigger = tag.Value;
                }
            }

            return new Recorded(instrument.Name, value, queue, trigger);
        }
    }

    private sealed class FakeSource(string queue, IReadOnlyList<RunQueueSample>? samples) : IRunQueueMetricsSource
    {
        public string Queue => queue;

        public Task<IReadOnlyList<RunQueueSample>> SampleAsync(CancellationToken cancellationToken) =>
            samples is null ? throw new NpgsqlException("connection refused") : Task.FromResult(samples);
    }
}
