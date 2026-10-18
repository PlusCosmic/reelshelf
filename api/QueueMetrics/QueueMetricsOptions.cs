namespace Reelshelf.QueueMetrics;

public sealed class QueueMetricsOptions
{
    public const string SectionName = "QueueMetrics";

    /// <summary>Exports nothing; the gauges can still be read with <c>dotnet-counters</c>.</summary>
    public const string NoExporter = "none";

    /// <summary>Pushes the gauges over OTLP to <see cref="QueueMetricsOtlpOptions.Endpoint"/>.</summary>
    public const string OtlpExporter = "otlp";

    /// <summary>Samples the run tables. Read when the API starts.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often the run tables are sampled. Read when the API starts.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary><see cref="NoExporter"/> (the default) or <see cref="OtlpExporter"/>. Read when the API starts.</summary>
    public string Exporter { get; set; } = NoExporter;

    public QueueMetricsOtlpOptions Otlp { get; set; } = new();
}

public sealed class QueueMetricsOtlpOptions
{
    /// <summary>
    /// The collector's URL, e.g. <c>http://otel-collector:4317</c> for gRPC or
    /// <c>https://otlp-gateway.example/otlp/v1/metrics</c> for HTTP. Empty falls back to
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>, then the OpenTelemetry default (<c>http://localhost:4317</c>).
    /// </summary>
    public string Endpoint { get; set; } = "";

    /// <summary>
    /// <c>grpc</c> or <c>http/protobuf</c>. Empty falls back to <c>OTEL_EXPORTER_OTLP_PROTOCOL</c>, then gRPC. With
    /// <c>http/protobuf</c>, <see cref="Endpoint"/> is used as given, so it must end in <c>/v1/metrics</c>.
    /// </summary>
    public string Protocol { get; set; } = "";

    /// <summary>
    /// Headers sent with every export, as <c>name=value</c> pairs separated by commas (e.g. an
    /// <c>Authorization</c> header for a hosted collector). Empty falls back to <c>OTEL_EXPORTER_OTLP_HEADERS</c>.
    /// </summary>
    public string Headers { get; set; } = "";

    /// <summary>How often the latest samples are pushed.</summary>
    public TimeSpan ExportInterval { get; set; } = TimeSpan.FromSeconds(60);
}
