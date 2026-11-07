using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Reelshelf.Core;

/// <summary>
/// <c>Shutdown:*</c>. A rolling deploy stops the old container once the new one is healthy, and Traefik keeps
/// sending it requests until Docker marks it unhealthy or it exits. With <see cref="DrainDelay"/> set, the instance
/// answers <c>/health</c> with 503 as soon as it is told to stop and keeps serving for that long before it stops
/// listening, so Traefik moves its traffic to the new container first. Zero (the default) stops at once, as dev's
/// <c>dotnet watch</c> wants.
/// </summary>
public sealed class ShutdownDrainOptions
{
    public const string SectionName = "Shutdown";

    /// <summary>How long to keep serving after the stop signal; longer than the healthcheck takes to fail.</summary>
    public TimeSpan DrainDelay { get; set; } = TimeSpan.Zero;

    /// <summary>What the host then gets to stop everything else (in-flight requests, background services).</summary>
    public TimeSpan StopTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>Whether this instance has been told to stop and is draining.</summary>
public sealed class ShutdownDrainState
{
    private volatile bool _draining;

    public bool IsDraining => _draining;

    public void Start() => _draining = true;
}

/// <summary>
/// Runs before every hosted service's <c>StopAsync</c> (Kestrel's included), so requests are still served while it
/// waits.
/// </summary>
public sealed class ShutdownDrainService(
    ShutdownDrainState state,
    IOptions<ShutdownDrainOptions> options,
    ILogger<ShutdownDrainService> logger) : IHostedLifecycleService
{
    public async Task StoppingAsync(CancellationToken cancellationToken)
    {
        TimeSpan delay = options.Value.DrainDelay;
        if (delay <= TimeSpan.Zero) return;

        state.Start();
        logger.LogInformation("Stopping: reporting unhealthy and serving for {DrainDelay} more before shutting down",
            delay);
        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The host's shutdown timeout ran out first; stop now.
        }
    }

    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Unhealthy while draining, so <c>/health</c> answers 503 and the container's healthcheck fails.</summary>
public sealed class ShutdownDrainHealthCheck(ShutdownDrainState state) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(state.IsDraining
            ? HealthCheckResult.Unhealthy("Shutting down")
            : HealthCheckResult.Healthy());
}

public static class ShutdownDrainSetup
{
    public static void AddShutdownDrain(this WebApplicationBuilder builder, IHealthChecksBuilder healthChecks)
    {
        IConfigurationSection section = builder.Configuration.GetSection(ShutdownDrainOptions.SectionName);
        builder.Services.Configure<ShutdownDrainOptions>(section);
        ShutdownDrainOptions current = section.Get<ShutdownDrainOptions>() ?? new ShutdownDrainOptions();
        if (current.DrainDelay < TimeSpan.Zero || current.StopTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "Shutdown:DrainDelay must not be negative and Shutdown:StopTimeout must be positive");
        }

        // The host's timeout covers the drain too, so it gets both.
        builder.Services.Configure<HostOptions>(host => host.ShutdownTimeout = current.DrainDelay + current.StopTimeout);
        builder.Services.AddSingleton<ShutdownDrainState>();
        builder.Services.AddHostedService<ShutdownDrainService>();
        healthChecks.AddCheck<ShutdownDrainHealthCheck>("draining");
    }
}
