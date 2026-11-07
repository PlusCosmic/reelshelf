using Microsoft.Extensions.Options;

namespace Reelshelf.Devices;

/// <summary>
/// Deletes sign-in requests past their expiry, revokes device tokens unused for
/// <see cref="DeviceOptions.RevokeUnusedAfter"/>, and deletes the rows of devices signed out since the last sweep.
/// </summary>
public sealed class DeviceTokenSweeper(
    DeviceStatements statements,
    IOptionsMonitor<DeviceOptions> options,
    ILogger<DeviceTokenSweeper> logger)
{
    public async Task<(int ExpiredRequests, int RevokedTokens)> SweepAsync()
    {
        int expired = await statements.DeleteExpiredAuthorizations();
        int revoked = await statements.RevokeUnused(options.CurrentValue.RevokeUnusedAfter);
        int deleted = await statements.DeleteRevoked();
        if (expired > 0 || revoked > 0 || deleted > 0)
        {
            logger.LogInformation(
                "Deleted {ExpiredRequests} expired device sign-ins, revoked {RevokedTokens} unused device tokens " +
                "and deleted {DeletedTokens} signed-out devices",
                expired, revoked, deleted);
        }

        return (expired, revoked);
    }
}

/// <summary>Runs <see cref="DeviceTokenSweeper"/> every <see cref="DeviceOptions.SweepInterval"/>; never in <c>OpenApi</c>.</summary>
public sealed class DeviceTokenSweepService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<DeviceOptions> options,
    ILogger<DeviceTokenSweepService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // A while after startup, so a deploy's restart doesn't sweep before the rest of the app is up.
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<DeviceTokenSweeper>().SweepAsync();
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    logger.LogError(error, "The device token sweep failed");
                }

                await Task.Delay(options.CurrentValue.SweepInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
