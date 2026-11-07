using System.Collections.Concurrent;

namespace Reelshelf.Discord.DiscordActivity;

/// <summary>
/// The result of a change to a room. <see cref="Applied"/> says whether the change itself was allowed;
/// <see cref="Changed"/> whether anything the room shows changed (the change, or someone's grace running out
/// first), so the new state should go to everyone. <see cref="State"/> is null when the room closed.
/// </summary>
public sealed record WatchRoomUpdate(bool Applied, bool Changed, RoomStateView? State);

/// <summary>
/// Every open watch room, keyed by Discord's Activity instance id, kept in Redis (<see cref="IWatchRoomStore"/>) so
/// rooms outlive a deploy and any API instance can serve any room. Each change loads the room, applies the change to
/// it and saves it only if nobody else saved first, trying again from the newer room when someone did, so changes
/// from two instances never overwrite each other.
/// </summary>
public sealed class WatchRoomRegistry(
    IWatchRoomStore store,
    WatchRoomServer server,
    ReactionLimiter reactions,
    TimeProvider timeProvider)
{
    /// <summary>A room changed under us this many times in a row is given up on; a busy room sees a handful at most.</summary>
    private const int MaxAttempts = 20;

    /// <summary>Adds a connection on this API instance to the room, opening the room if nobody is in it.</summary>
    public async Task<RoomStateView> JoinAsync(string instanceId, RoomConnection connection)
    {
        RoomConnection joining = connection with { ServerId = server.Id };
        WatchRoomUpdate? update = await UpdateAsync(instanceId, room =>
        {
            room.Join(joining);
            return true;
        }, create: true);
        return update?.State ?? throw new InvalidOperationException($"Couldn't join watch room {instanceId}");
    }

    /// <summary>Marks a connection dropped; the person stays in the room for <see cref="WatchRoom.ReconnectGrace"/>.</summary>
    public Task<WatchRoomUpdate?> LeaveAsync(string instanceId, string connectionId) =>
        UpdateAsync(instanceId, room =>
        {
            room.Leave(connectionId);
            return true;
        }, create: false);

    /// <summary>The room as stored now, for reading only; null when nobody is in it.</summary>
    public async Task<WatchRoom?> FindAsync(string instanceId) =>
        await store.LoadAsync(instanceId) is { } json ? WatchRoom.FromJson(instanceId, json, timeProvider) : null;

    /// <summary>
    /// Runs <paramref name="change"/> on the room and saves it, returning the state it made, with no other change in
    /// between. The change may run more than once, on a newer room each time, when another change got there first.
    /// Null when the room is gone.
    /// </summary>
    public Task<WatchRoomUpdate?> ApplyAsync(string instanceId, Func<WatchRoom, bool> change) =>
        UpdateAsync(instanceId, change, create: false);

    /// <summary>
    /// A reaction to the playing clip (<see cref="WatchRoom.React"/>), or null when it is refused, including when the
    /// person has sent too many in the last few seconds.
    /// </summary>
    public async Task<RoomReaction?> ReactAsync(string instanceId, string connectionId, string emoji)
    {
        if (await FindAsync(instanceId) is not { } room || room.React(connectionId, emoji) is not { } reaction)
        {
            return null;
        }

        return reactions.TryTake(instanceId, reaction.DiscordUserId) ? reaction : null;
    }

    /// <summary>
    /// Visits every room: moves on any whose playing clip is overdue (<see cref="WatchRoom.AdvanceIfOverdue"/>), takes
    /// out people whose grace is up and closes rooms nobody is left in. Returns the rooms whose state changed, for
    /// <see cref="WatchRoomClock"/> to broadcast. Every API instance does this; a room changed by one is already moved
    /// on when the next looks at it.
    /// </summary>
    public async Task<List<(string InstanceId, RoomStateView State)>> TickAsync()
    {
        List<(string, RoomStateView)> changed = [];
        foreach (string instanceId in await store.ListAsync())
        {
            WatchRoomUpdate? update = await UpdateAsync(instanceId, room => room.AdvanceIfOverdue(), create: false);
            if (update is { Changed: true, State: { } state })
            {
                changed.Add((instanceId, state));
            }
        }

        return changed;
    }

    private async Task<WatchRoomUpdate?> UpdateAsync(string instanceId, Func<WatchRoom, bool> change, bool create)
    {
        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            string? stored = await store.LoadAsync(instanceId);
            if (stored is null && !create)
            {
                return null;
            }

            WatchRoom room = stored is null
                ? new WatchRoom(instanceId, timeProvider)
                : WatchRoom.FromJson(instanceId, stored, timeProvider);
            long version = room.Version;

            List<string> others = room.LiveServerIds.Where(id => id != server.Id).ToList();
            IReadOnlySet<string> alive = others.Count == 0
                ? new HashSet<string>()
                : await store.AliveServersAsync(others);
            room.Prune(id => id == server.Id || alive.Contains(id));

            bool applied = change(room);
            if (room.IsEmpty)
            {
                if (stored is null || await store.TryDeleteAsync(instanceId, stored))
                {
                    return new WatchRoomUpdate(applied, true, null);
                }

                continue;
            }

            string json = room.ToJson();
            if (json == stored || await store.TrySaveAsync(instanceId, stored, json))
            {
                return new WatchRoomUpdate(applied, room.Version != version, room.Snapshot());
            }
        }

        throw new InvalidOperationException($"Watch room {instanceId} kept changing; gave up after {MaxAttempts} tries");
    }
}

/// <summary>
/// This API instance, as the rooms know it. Each connection records the instance holding it, and each instance
/// renews <see cref="Id"/> in Redis every <see cref="HeartbeatInterval"/> (<see cref="WatchRoomClock"/>), so when an
/// instance stops without closing its connections (a crash, a killed container) the others see its heartbeat lapse
/// and give its connections their grace.
/// </summary>
public sealed class WatchRoomServer
{
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);

    /// <summary>How long a heartbeat lasts in Redis: three missed in a row and the instance counts as gone.</summary>
    public static readonly TimeSpan HeartbeatLifetime = TimeSpan.FromSeconds(30);

    public string Id { get; } = Guid.NewGuid().ToString("N");
}

/// <summary>
/// How many reactions each person sent lately, per room (<see cref="WatchRoom.ReactionsPerWindow"/> per
/// <see cref="WatchRoom.ReactionWindow"/>). Reactions are never stored, so this is kept in memory on each API
/// instance: while a deploy overlaps two, someone could send twice the limit for a few seconds.
/// </summary>
public sealed class ReactionLimiter(TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _recent = new(StringComparer.Ordinal);

    public bool TryTake(string instanceId, string discordUserId)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        Queue<DateTimeOffset> recent = _recent.GetOrAdd($"{instanceId}\n{discordUserId}", _ => new Queue<DateTimeOffset>());
        lock (recent)
        {
            Expire(recent, now);
            if (recent.Count >= WatchRoom.ReactionsPerWindow)
            {
                return false;
            }

            recent.Enqueue(now);
            return true;
        }
    }

    /// <summary>Forgets people who haven't reacted within the window, so the counts don't grow for ever.</summary>
    public void ForgetIdle()
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        foreach ((string key, Queue<DateTimeOffset> recent) in _recent)
        {
            lock (recent)
            {
                Expire(recent, now);
                if (recent.Count == 0)
                {
                    _recent.TryRemove(new KeyValuePair<string, Queue<DateTimeOffset>>(key, recent));
                }
            }
        }
    }

    private static void Expire(Queue<DateTimeOffset> recent, DateTimeOffset now)
    {
        while (recent.Count > 0 && now - recent.Peek() >= WatchRoom.ReactionWindow)
        {
            recent.Dequeue();
        }
    }
}
