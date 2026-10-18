using Microsoft.Extensions.Options;
using Npgsql;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using Reelshelf.ApexLegends.LegendDetection;
using Reelshelf.ClipSummary;
using Reelshelf.ClipTranscription;

namespace Reelshelf.QueueMetrics;

public static class QueueMetricsSetup
{
    public static readonly RunTableQueue LegendDetectionQueue = new("legend_detection", "legend_detection_run",
        LegendDetectionService.RetryAfter, LegendDetectionService.StaleAfter,
        [
            LegendDetectionStatements.AutoTrigger, LegendDetectionStatements.ManualTrigger,
            LegendDetectionStatements.EscalationTrigger, LegendDetectionStatements.EvalTrigger
        ],
        // Archived runs are ignored by the worker, so they are not part of the queue.
        "archived_at IS NULL");

    public static readonly RunTableQueue ClipTranscriptionQueue = new("clip_transcription", "clip_transcription_run",
        ClipTranscriptionService.RetryAfter, ClipTranscriptionService.StaleAfter,
        [
            ClipTranscriptionStatements.AutoTrigger, ClipTranscriptionStatements.ManualTrigger,
            ClipTranscriptionStatements.RetryTrigger
        ]);

    public static readonly RunTableQueue ClipSummaryQueue = new("clip_summary", "clip_summary_run",
        ClipSummaryService.RetryAfter, ClipSummaryService.StaleAfter,
        [ClipSummaryStatements.AutoTrigger, ClipSummaryStatements.ManualTrigger, ClipSummaryStatements.RetryTrigger]);

    /// <summary>
    /// Registers the run queues' gauges, the service that samples them, and the exporter
    /// <see cref="QueueMetricsOptions.Exporter"/> names. Neither the sampler nor an exporter runs in the
    /// <c>OpenApi</c> environment.
    /// </summary>
    public static void AddQueueMetrics(this WebApplicationBuilder builder)
    {
        IConfigurationSection section = builder.Configuration.GetSection(QueueMetricsOptions.SectionName);
        builder.Services.Configure<QueueMetricsOptions>(section);
        builder.Services.AddMetrics();
        builder.Services.AddSingleton<RunQueueMetrics>();

        // A run table with the usual columns is one more line here; one shaped differently registers its own
        // IRunQueueMetricsSource instead, as transcode_run does (type, available_at, heartbeat_at, finished_at).
        builder.Services.AddRunTableQueueMetrics<LegendDetectionOptions>(LegendDetectionQueue, o => o.MaxAttempts);
        builder.Services.AddRunTableQueueMetrics<ClipTranscriptionOptions>(ClipTranscriptionQueue, o => o.MaxAttempts);
        builder.Services.AddRunTableQueueMetrics<ClipSummaryOptions>(ClipSummaryQueue, o => o.MaxAttempts);
        builder.Services.AddSingleton<IRunQueueMetricsSource, TranscodeRunMetricsSource>();

        if (builder.Environment.IsEnvironment("OpenApi"))
        {
            return;
        }

        QueueMetricsOptions settings = section.Get<QueueMetricsOptions>() ?? new QueueMetricsOptions();
        if (settings.Enabled)
        {
            builder.Services.AddHostedService<QueueMetricsBackgroundService>();
        }

        AddExporter(builder, settings);
    }

    /// <summary>
    /// Registers a queue kept in a run table with the usual columns, whose attempt limit is read from
    /// <typeparamref name="TOptions"/> at each sample.
    /// </summary>
    public static IServiceCollection AddRunTableQueueMetrics<TOptions>(
        this IServiceCollection services,
        RunTableQueue queue,
        Func<TOptions, int> maxAttempts) where TOptions : class
    {
        return services.AddSingleton<IRunQueueMetricsSource>(sp =>
        {
            IOptionsMonitor<TOptions> options = sp.GetRequiredService<IOptionsMonitor<TOptions>>();
            return new RunTableMetricsSource(queue, sp.GetRequiredService<NpgsqlDataSource>(),
                () => maxAttempts(options.CurrentValue));
        });
    }

    private static void AddExporter(WebApplicationBuilder builder, QueueMetricsOptions settings)
    {
        string exporter = settings.Exporter.Trim().ToLowerInvariant();
        if (exporter is "" or QueueMetricsOptions.NoExporter)
        {
            // The meter still exists, so dotnet-counters can read it: dotnet-counters monitor --counters Reelshelf.Queues
            return;
        }

        if (exporter != QueueMetricsOptions.OtlpExporter)
        {
            throw new InvalidOperationException(
                $"QueueMetrics:Exporter must be '{QueueMetricsOptions.NoExporter}' or '{QueueMetricsOptions.OtlpExporter}', not '{settings.Exporter}'");
        }

        QueueMetricsOtlpOptions otlp = settings.Otlp;
        OtlpExportProtocol? protocol = otlp.Protocol.Trim().ToLowerInvariant() switch
        {
            "" => null,
            "grpc" => OtlpExportProtocol.Grpc,
            "http/protobuf" => OtlpExportProtocol.HttpProtobuf,
            _ => throw new InvalidOperationException(
                $"QueueMetrics:Otlp:Protocol must be 'grpc' or 'http/protobuf', not '{otlp.Protocol}'")
        };
        Uri? endpoint = null;
        if (!string.IsNullOrWhiteSpace(otlp.Endpoint) && !Uri.TryCreate(otlp.Endpoint.Trim(), UriKind.Absolute, out endpoint))
        {
            throw new InvalidOperationException($"QueueMetrics:Otlp:Endpoint '{otlp.Endpoint}' is not an absolute URL");
        }

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService("reelshelf")
                .AddAttributes([new("deployment.environment.name", builder.Environment.EnvironmentName)]))
            .WithMetrics(metrics => metrics
                .AddMeter(RunQueueMetrics.MeterName)
                .AddOtlpExporter((exporterOptions, readerOptions) =>
                {
                    if (endpoint is not null)
                    {
                        exporterOptions.Endpoint = endpoint;
                    }

                    if (protocol is not null)
                    {
                        exporterOptions.Protocol = protocol.Value;
                    }

                    if (!string.IsNullOrWhiteSpace(otlp.Headers))
                    {
                        exporterOptions.Headers = otlp.Headers.Trim();
                    }

                    readerOptions.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds =
                        (int)Math.Max(1000, otlp.ExportInterval.TotalMilliseconds);
                }));
    }
}
