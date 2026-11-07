using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Reelshelf.Core;
using Xunit;

namespace Reelshelf.Test.Core;

/// <summary>A stopping instance reports unhealthy and keeps serving for the drain delay, so Traefik moves off it first.</summary>
public class ShutdownDrainTests
{
    [Fact]
    public async Task Stopping_ReportsUnhealthy_AndWaitsTheDrainDelay()
    {
        ShutdownDrainState state = new();
        ShutdownDrainHealthCheck check = new(state);
        ShutdownDrainService service = new(state,
            Options.Create(new ShutdownDrainOptions { DrainDelay = TimeSpan.FromMilliseconds(200) }),
            NullLogger<ShutdownDrainService>.Instance);

        Assert.Equal(HealthStatus.Healthy, (await check.CheckHealthAsync(new HealthCheckContext())).Status);

        Task stopping = service.StoppingAsync(CancellationToken.None);
        Assert.Equal(HealthStatus.Unhealthy, (await check.CheckHealthAsync(new HealthCheckContext())).Status);
        Assert.False(stopping.IsCompleted);

        await stopping.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void NoDrainDelay_StopsAtOnce_AndStaysHealthy()
    {
        ShutdownDrainState state = new();
        ShutdownDrainService service = new(state, Options.Create(new ShutdownDrainOptions()),
            NullLogger<ShutdownDrainService>.Instance);

        Assert.True(service.StoppingAsync(CancellationToken.None).IsCompletedSuccessfully);
        Assert.False(state.IsDraining);
    }

    [Fact]
    public async Task ShutdownTimeout_CutsTheDrainShort()
    {
        ShutdownDrainService service = new(new ShutdownDrainState(),
            Options.Create(new ShutdownDrainOptions { DrainDelay = TimeSpan.FromMinutes(5) }),
            NullLogger<ShutdownDrainService>.Instance);
        using CancellationTokenSource timeout = new(TimeSpan.FromMilliseconds(100));

        await service.StoppingAsync(timeout.Token).WaitAsync(TimeSpan.FromSeconds(5));
    }
}
