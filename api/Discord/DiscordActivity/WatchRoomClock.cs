using Microsoft.AspNetCore.SignalR;

namespace Reelshelf.Discord.DiscordActivity;

/// <summary>
/// The watch rooms' clock: every second it moves on any room whose playing clip is <see cref="WatchRoom.EndGrace"/>
/// past its expected end (<see cref="WatchRoomRegistry.AdvanceOverdue"/>) and broadcasts the new state, so a room
/// never waits for ever on a host whose player failed or was refused autoplay.
/// </summary>
public sealed class WatchRoomClock(
    WatchRoomRegistry rooms,
    IHubContext<WatchRoomHub> hub,
    TimeProvider timeProvider,
    ILogger<WatchRoomClock> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(Interval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach ((WatchRoom room, RoomStateView state) in rooms.AdvanceOverdue())
            {
                try
                {
                    await hub.Clients.Group(WatchRoomHub.GroupName(room.InstanceId))
                        .SendAsync(WatchRoomHub.RoomStateMethod, state, stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "Couldn't send watch room {InstanceId} its next clip", room.InstanceId);
                }
            }
        }
    }
}
