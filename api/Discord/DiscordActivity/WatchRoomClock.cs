using Microsoft.AspNetCore.SignalR;

namespace Reelshelf.Discord.DiscordActivity;

/// <summary>
/// The watch rooms' clock. Every second it visits every room (<see cref="WatchRoomRegistry.TickAsync"/>): a playing
/// clip <see cref="WatchRoom.EndGrace"/> past its expected end moves on, so a room never waits for ever on a host
/// whose player failed or was refused autoplay, and people whose <see cref="WatchRoom.ReconnectGrace"/> is up leave.
/// It broadcasts each changed room's state through the backplane, so it reaches connections on every API instance.
/// It also keeps this instance's heartbeat (<see cref="WatchRoomServer"/>), written before the app takes connections
/// and deleted when it stops.
/// </summary>
public sealed class WatchRoomClock(
    WatchRoomRegistry rooms,
    IWatchRoomStore store,
    WatchRoomServer server,
    ReactionLimiter reactions,
    IHubContext<WatchRoomHub> hub,
    TimeProvider timeProvider,
    ILogger<WatchRoomClock> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private DateTimeOffset _lastHeartbeat = DateTimeOffset.MinValue;

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        // Before the server listens, so no other instance sees this one's first connections without a heartbeat.
        await HeartbeatAsync();
        await base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        try
        {
            // Connections still recorded here get their grace at once, rather than when the heartbeat lapses.
            await store.MarkServerGoneAsync(server.Id).WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Couldn't remove watch room heartbeat {ServerId}", server.Id);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(Interval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (timeProvider.GetUtcNow() - _lastHeartbeat >= WatchRoomServer.HeartbeatInterval)
            {
                await HeartbeatAsync();
            }

            reactions.ForgetIdle();

            List<(string InstanceId, RoomStateView State)> changed;
            try
            {
                changed = await rooms.TickAsync();
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Couldn't check the watch rooms");
                continue;
            }

            foreach ((string instanceId, RoomStateView state) in changed)
            {
                try
                {
                    await hub.Clients.Group(WatchRoomHub.GroupName(instanceId))
                        .SendAsync(WatchRoomHub.RoomStateMethod, state, stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "Couldn't send watch room {InstanceId} its new state", instanceId);
                }
            }
        }
    }

    private async Task HeartbeatAsync()
    {
        try
        {
            await store.MarkServerAliveAsync(server.Id, WatchRoomServer.HeartbeatLifetime);
            _lastHeartbeat = timeProvider.GetUtcNow();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Couldn't renew watch room heartbeat {ServerId}", server.Id);
        }
    }
}
