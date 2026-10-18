using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Reelshelf.Worker;

// The transcode worker (docs/video-pipeline/workers-and-jobs.md): claims jobs from the API, encodes with ffmpeg and
// uploads through presigned URLs. Configured from the Worker section, usually as Worker__* environment variables.
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddSimpleConsole(console =>
{
    console.SingleLine = true;
    console.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
    console.UseUtcTimestamp = true;
});

builder.Services.AddTranscodeWorker(builder.Configuration);

// SIGTERM gives the running job its grace period, then time to report.
WorkerSettings configured = builder.Configuration.GetSection(WorkerSettings.SectionName).Get<WorkerSettings>() ?? new();
builder.Services.Configure<HostOptions>(host => host.ShutdownTimeout = configured.ShutdownGrace + TimeSpan.FromSeconds(15));

await builder.Build().RunAsync();
