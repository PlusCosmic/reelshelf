using Amazon.ECS;

namespace Reelshelf.Video.Transcoding.Overflow;

public static class OverflowSetup
{
    /// <summary>
    /// Registers the overflow pool where <c>Overflow:Enabled</c> is true, outside the <c>OpenApi</c> environment
    /// (which builds the app without secrets and runs no background services). Startup fails when it is on and any
    /// of the cluster, task definition, capacity provider or region is missing (or, on Fargate, the subnets or security
    /// groups), so a half-configured pool
    /// never runs. Call it after the R2 host, whose storage gates each tick.
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

        builder.Services.AddSingleton<IAmazonECS>(_ => EcsOverflowTaskLauncher.CreateClient(settings.Aws));
        builder.Services.AddSingleton<IOverflowTaskLauncher, EcsOverflowTaskLauncher>();
        builder.Services.AddSingleton<IOverflowBacklogReader, OverflowBacklogStatements>();
        builder.Services.AddSingleton<OverflowMetrics>();
        builder.Services.AddSingleton<OverflowScaler>();
        builder.Services.AddHostedService<OverflowBackgroundService>();
    }
}
