using Reelshelf.Billing;
using Reelshelf.Video.R2;

namespace Reelshelf.Plans;

public static class PlanSetup
{
    /// <summary>
    /// Going over the limit and exports (ADR-0007). Exports need R2, where their zips go; without it they are neither
    /// made nor offered. The sweep and the export service never run in the <c>OpenApi</c> environment.
    /// </summary>
    public static void AddPlanEnforcement(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<PlanOptions>(builder.Configuration.GetSection(PlanOptions.SectionName));
        builder.Services.AddScoped<IAccountPlans>(provider => provider.GetRequiredService<AccountPlanService>());
        builder.Services.AddScoped<IPlanEnforcementStore, PlanEnforcementStatements>();
        builder.Services.AddScoped<IClipExportStore, ClipExportStatements>();
        builder.Services.AddScoped<IOwnedClipDeleter, ClipServiceDeleter>();
        builder.Services.AddScoped<IPlanEndingNotifier, PlanEndingNotifier>();
        builder.Services.AddSingleton<IExportStorage>(provider =>
            provider.GetService<R2Storage>() is { } r2 ? new R2ExportStorage(r2) : new UnconfiguredExportStorage());
        builder.Services.AddSingleton<IClipVideoReader>(provider =>
            provider.GetService<R2Storage>() is { } r2 ? new R2VideoReader(r2) : new NoVideoReader());
        builder.Services.AddHttpClient(ClipExportVideoSource.HttpClientName,
            client => client.Timeout = TimeSpan.FromMinutes(30));
        builder.Services.AddScoped<IExportVideoSource, ClipExportVideoSource>();
        builder.Services.AddScoped<ClipExportBuilder>();
        builder.Services.AddScoped<ClipExportProcessor>();
        builder.Services.AddScoped<OverLimitSweeper>();

        if (!builder.Environment.IsEnvironment("OpenApi"))
        {
            builder.Services.AddHostedService<ClipExportBackgroundService>();
            builder.Services.AddHostedService<OverLimitSweepService>();
        }
    }
}
