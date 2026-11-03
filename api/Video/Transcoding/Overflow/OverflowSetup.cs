namespace Reelshelf.Video.Transcoding.Overflow;

public static class OverflowSetup
{
    /// <summary>
    /// Registers the overflow pool where <c>Overflow:Enabled</c> is true, outside the <c>OpenApi</c> environment
    /// (which builds the app without secrets and runs no background services), with a launcher for each backend in
    /// <c>Overflow:Pools</c> (ECS on Fargate Spot, Google Cloud Batch), in that order. Startup fails when it is on and
    /// the settings are incomplete (<see cref="OverflowOptions.Problems"/>), so a half-configured pool never runs. Call
    /// it after the R2 host, whose storage gates each tick.
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

        IReadOnlyList<string> poolNames = settings.PoolList;
        if (poolNames.Contains(OverflowOptions.EcsPool))
        {
            builder.Services.AddSingleton<IEcsTasks>(_ => new AwsEcsTasks(AwsEcsTasks.CreateClient(settings.Ecs)));
            builder.Services.AddSingleton<EcsOverflowTaskLauncher>();
        }

        if (poolNames.Contains(OverflowOptions.BatchPool))
        {
            builder.Services.AddSingleton<IBatchJobs>(_ => new GoogleBatchJobs(GoogleBatchJobs.CreateClient(settings.Batch)));
            builder.Services.AddSingleton<BatchOverflowTaskLauncher>();
        }

        builder.Services.AddSingleton(provider => new OverflowPools(poolNames.Select(name => new OverflowPool(name,
            name switch
            {
                OverflowOptions.EcsPool => provider.GetRequiredService<EcsOverflowTaskLauncher>(),
                OverflowOptions.BatchPool => (IOverflowTaskLauncher)provider.GetRequiredService<BatchOverflowTaskLauncher>(),
                _ => throw new InvalidOperationException($"Unknown overflow pool {name}")
            })).ToList()));

        builder.Services.AddSingleton<IOverflowBacklogReader, OverflowBacklogStatements>();
        builder.Services.AddSingleton<OverflowMetrics>();
        builder.Services.AddSingleton<OverflowScaler>();
        builder.Services.AddHostedService<OverflowBackgroundService>();
    }
}
