namespace Reelshelf.Video.Transcoding.Overflow;

public static class OverflowSetup
{
    /// <summary>
    /// Registers the overflow pool where <c>Overflow:Enabled</c> is true, outside the <c>OpenApi</c> environment
    /// (which builds the app without secrets and runs no background services), with the Google Cloud Batch launcher.
    /// Startup fails when it is on and the settings are incomplete
    /// (<see cref="OverflowOptions.Problems"/>), so a half-configured pool never runs. Call it after the R2 host, whose
    /// storage gates each tick.
    /// </summary>
    public static void AddTranscodeOverflow(this WebApplicationBuilder builder)
    {
        IConfigurationSection section = builder.Configuration.GetSection(OverflowOptions.SectionName);
        builder.Services.Configure<OverflowOptions>(section);
        if (builder.Environment.IsEnvironment("OpenApi"))
        {
            return;
        }

        OverflowOptions settings = section.Get<OverflowOptions>() ?? new OverflowOptions();
        if (!settings.Enabled)
        {
            return;
        }

        IReadOnlyList<string> problems = settings.Problems();
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"Overflow:Enabled is on, but its settings are incomplete: {string.Join("; ", problems)}");
        }

        builder.Services.AddSingleton<IBatchJobs>(_ => new GoogleBatchJobs(GoogleBatchJobs.CreateClient(settings.Batch)));
        builder.Services.AddSingleton<IOverflowTaskLauncher, BatchOverflowTaskLauncher>();

        builder.Services.AddSingleton<IOverflowBacklogReader, OverflowBacklogStatements>();
        builder.Services.AddSingleton<OverflowMetrics>();
        builder.Services.AddSingleton<OverflowScaler>();
        builder.Services.AddHostedService<OverflowBackgroundService>();
    }
}
