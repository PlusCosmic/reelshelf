using System.Collections.Concurrent;

namespace Reelshelf.DiscordActivity;

/// <summary>One hub connection in a room. A person with the Activity open twice (desktop and phone) has two.</summary>
public sealed record RoomConnection(
    string ConnectionId,
    string DiscordUserId,
    string Name,
    string? AvatarUrl,
    Guid? AccountId,
    string? AccountName,
    DateTimeOffset ConnectedAt)
{
    public bool IsMember => AccountId is not null;
}

/// <summary>
/// The clip on screen. Guests see only this much of it: title, game and whose shelf it came from, plus the
/// video id the player streams.
/// </summary>
public sealed record NowPlaying(
    Guid ClipId,
    Guid VideoId,
    string Title,
    string Game,
    string OwnerName,
    int DurationSeconds);

/// <summary>A clip waiting in the room's queue, with the Discord user who queued it (always its owner).</summary>
public sealed record QueuedClip(Guid ItemId, NowPlaying Clip, string OwnerDiscordUserId);

/// <summary>
/// A queued clip as clients see it. It carries no video id: the stream is only handed out once the clip plays.
/// </summary>
public sealed record QueueItemView(
    Guid ItemId,
    Guid ClipId,
    string Title,
    string Game,
    string OwnerName,
    string OwnerDiscordUserId,
    int DurationSeconds);

/// <summary>
/// Where playback stood at <see cref="UpdatedAt"/> (server time). While playing, clients add the time since
/// then to <see cref="PositionSeconds"/> to find where everyone should be now. <see cref="ItemId"/> names this
/// play of the clip, so a clip queued again later is a new item.
/// </summary>
public sealed record RoomPlayback(
    Guid ItemId,
    NowPlaying Clip,
    string OwnerDiscordUserId,
    bool Playing,
    double PositionSeconds,
    DateTimeOffset UpdatedAt);

public sealed record RoomParticipantView(string DiscordUserId, string Name, string? AvatarUrl, bool IsMember, bool IsHost);

/// <summary>
/// The whole room as clients see it. <see cref="Version"/> rises with every change, so a client can drop a
/// state that arrives after a newer one.
/// </summary>
public sealed record RoomStateView(
    long Version,
    string? HostDiscordUserId,
    List<RoomParticipantView> Participants,
    RoomPlayback? Playback,
    List<QueueItemView> Queue,
    bool QueueLocked,
    DateTimeOffset ServerTime);

public enum EnqueueResult
{
    Queued,
    NotAMember,
    Locked,
    AlreadyQueued,
    Full
}

/// <summary>
/// A watch room: everyone connected to one Activity instance, its host, what is playing and what is queued.
/// Held in memory only (ADR-0006). Every method takes the room's lock, so the hub can call it from any
/// connection.
/// </summary>
/// <remarks>
/// A queued clip is shown to the room only while its owner is in it: clips of an owner who has left are hidden
/// from the queue and dropped when their turn comes, and come back if the owner reconnects first. A clip that
/// is already playing plays on. When no member is left, the room has nothing to play and its queue empties.
/// </remarks>
public sealed class WatchRoom(string instanceId, TimeProvider timeProvider)
{
    public const int MaxQueueLength = 50;

    private readonly object _gate = new();
    private readonly Dictionary<string, RoomConnection> _connections = new(StringComparer.Ordinal);
    private readonly List<QueuedClip> _queue = [];
    private string? _hostDiscordUserId;
    private RoomPlayback? _playback;
    private bool _queueLocked;
    private long _version;

    public string InstanceId { get; } = instanceId;

    /// <summary>Set once the last connection leaves; a closed room is never joined again.</summary>
    internal bool Closed { get; private set; }

    internal bool TryJoin(RoomConnection connection)
    {
        lock (_gate)
        {
            if (Closed)
            {
                return false;
            }

            _connections[connection.ConnectionId] = connection;
            _version++;
            if (_hostDiscordUserId is null && connection.IsMember)
            {
                _hostDiscordUserId = connection.DiscordUserId;
            }

            return true;
        }
    }

    /// <summary>Removes a connection and returns true when the room is now empty (and closed).</summary>
    internal bool Leave(string connectionId)
    {
        lock (_gate)
        {
            if (!_connections.Remove(connectionId, out RoomConnection? left))
            {
                return false;
            }

            _version++;
            if (_connections.Count == 0)
            {
                Closed = true;
                return true;
            }

            // The host keeps the role while any of their connections remain; otherwise it passes to the
            // member who has been here longest. With only guests left there is no host, and no clips.
            if (left.DiscordUserId == _hostDiscordUserId && !IsPresent(left.DiscordUserId))
            {
                _hostDiscordUserId = _connections.Values
                    .Where(remaining => remaining.IsMember)
                    .OrderBy(remaining => remaining.ConnectedAt)
                    .Select(remaining => remaining.DiscordUserId)
                    .FirstOrDefault();
            }

            if (_hostDiscordUserId is null)
            {
                _playback = null;
                _queue.Clear();
                _queueLocked = false;
            }

            return false;
        }
    }

    public RoomConnection? GetConnection(string connectionId)
    {
        lock (_gate)
        {
            return _connections.GetValueOrDefault(connectionId);
        }
    }

    public bool IsHost(string connectionId)
    {
        lock (_gate)
        {
            return IsHostLocked(connectionId);
        }
    }

    /// <summary>
    /// A member queues one of their own clips. It goes in round-robin by who queued it: a member's first clip
    /// waits behind everyone else's first, their second behind everyone's second, and so on. When nothing is
    /// playing it starts at once. While the queue is locked only the host can add.
    /// </summary>
    public EnqueueResult Enqueue(string connectionId, NowPlaying clip)
    {
        lock (_gate)
        {
            if (!_connections.TryGetValue(connectionId, out RoomConnection? caller) || !caller.IsMember)
            {
                return EnqueueResult.NotAMember;
            }

            if (_queueLocked && caller.DiscordUserId != _hostDiscordUserId)
            {
                return EnqueueResult.Locked;
            }

            if (_queue.Any(item => item.Clip.ClipId == clip.ClipId))
            {
                return EnqueueResult.AlreadyQueued;
            }

            if (_queue.Count >= MaxQueueLength)
            {
                return EnqueueResult.Full;
            }

            QueuedClip queued = new(Guid.NewGuid(), clip, caller.DiscordUserId);
            _queue.Insert(RoundRobinIndex(caller.DiscordUserId), queued);
            if (_playback is null)
            {
                PlayNextLocked();
            }

            _version++;
            return EnqueueResult.Queued;
        }
    }

    /// <summary>The host can remove any queued clip; a member can remove their own.</summary>
    public bool Remove(string connectionId, Guid itemId)
    {
        lock (_gate)
        {
            int index = _queue.FindIndex(item => item.ItemId == itemId);
            if (index < 0 || !_connections.TryGetValue(connectionId, out RoomConnection? caller) ||
                (caller.DiscordUserId != _hostDiscordUserId &&
                 caller.DiscordUserId != _queue[index].OwnerDiscordUserId))
            {
                return false;
            }

            _queue.RemoveAt(index);
            _version++;
            return true;
        }
    }

    /// <summary>The host moves a queued clip to <paramref name="toIndex"/> among the clips the room can see.</summary>
    public bool Move(string connectionId, Guid itemId, int toIndex)
    {
        lock (_gate)
        {
            List<QueuedClip> visible = VisibleQueue();
            QueuedClip? moving = visible.FirstOrDefault(item => item.ItemId == itemId);
            if (!IsHostLocked(connectionId) || moving is null)
            {
                return false;
            }

            // Place it before the visible clip now at that position, or after the last one; hidden clips of
            // absent owners keep their places.
            visible.Remove(moving);
            _queue.Remove(moving);
            int target = Math.Clamp(toIndex, 0, visible.Count);
            int at = target < visible.Count ? _queue.IndexOf(visible[target]) : _queue.Count;
            _queue.Insert(at, moving);
            _version++;
            return true;
        }
    }

    /// <summary>The host locks the queue so only they can add, for a showcase.</summary>
    public bool SetQueueLocked(string connectionId, bool locked)
    {
        lock (_gate)
        {
            if (!IsHostLocked(connectionId))
            {
                return false;
            }

            _queueLocked = locked;
            _version++;
            return true;
        }
    }

    /// <summary>
    /// The host moves on to the next queued clip, or stops when the queue is empty. With
    /// <paramref name="currentItemId"/> it only moves on from that clip, so a clip ending on two of the host's
    /// devices skips once.
    /// </summary>
    public bool Next(string connectionId, Guid? currentItemId)
    {
        lock (_gate)
        {
            if (!IsHostLocked(connectionId) ||
                (currentItemId is not null && _playback?.ItemId != currentItemId))
            {
                return false;
            }

            PlayNextLocked();
            _version++;
            return true;
        }
    }

    /// <summary>The host plays a queued clip now, taking it out of the queue.</summary>
    public bool PlayNow(string connectionId, Guid itemId)
    {
        lock (_gate)
        {
            QueuedClip? item = VisibleQueue().FirstOrDefault(queued => queued.ItemId == itemId);
            if (!IsHostLocked(connectionId) || item is null)
            {
                return false;
            }

            _queue.Remove(item);
            Start(item);
            _version++;
            return true;
        }
    }

    /// <summary>
    /// The host plays, pauses or seeks the current clip. <paramref name="playing"/> null keeps the current
    /// play state (a seek). False when the caller is not the host or nothing is on.
    /// </summary>
    public bool SetPlayback(string connectionId, bool? playing, double positionSeconds)
    {
        lock (_gate)
        {
            if (!IsHostLocked(connectionId) || _playback is null || !double.IsFinite(positionSeconds))
            {
                return false;
            }

            double position = Math.Max(0, positionSeconds);
            if (_playback.Clip.DurationSeconds > 0)
            {
                position = Math.Min(position, _playback.Clip.DurationSeconds);
            }

            _playback = _playback with
            {
                Playing = playing ?? _playback.Playing,
                PositionSeconds = position,
                UpdatedAt = timeProvider.GetUtcNow()
            };
            _version++;
            return true;
        }
    }

    public RoomStateView Snapshot()
    {
        lock (_gate)
        {
            // One row per person, however many connections they have, in the order they arrived.
            Dictionary<string, RoomConnection> people = _connections.Values
                .GroupBy(connection => connection.DiscordUserId)
                .Select(group => group.OrderBy(connection => connection.ConnectedAt).First())
                .ToDictionary(connection => connection.DiscordUserId);

            List<RoomParticipantView> participants = people.Values
                .OrderBy(connection => connection.ConnectedAt)
                .Select(connection => new RoomParticipantView(
                    connection.DiscordUserId,
                    connection.Name,
                    connection.AvatarUrl,
                    connection.IsMember,
                    connection.DiscordUserId == _hostDiscordUserId))
                .ToList();

            List<QueueItemView> queue = VisibleQueue()
                .Select(item => new QueueItemView(
                    item.ItemId,
                    item.Clip.ClipId,
                    item.Clip.Title,
                    item.Clip.Game,
                    item.Clip.OwnerName,
                    item.OwnerDiscordUserId,
                    item.Clip.DurationSeconds))
                .ToList();

            return new RoomStateView(
                _version,
                _hostDiscordUserId,
                participants,
                _playback,
                queue,
                _queueLocked,
                timeProvider.GetUtcNow());
        }
    }

    /// <summary>
    /// Where a new clip from <paramref name="owner"/> goes. It is the owner's round <c>k</c>, where <c>k</c>
    /// is how many of their clips are already queued. It goes after the owner's own queued clips, before the
    /// first clip from a later round. The host's manual moves are kept, since nothing already queued moves.
    /// </summary>
    private int RoundRobinIndex(string owner)
    {
        int round = _queue.Count(item => item.OwnerDiscordUserId == owner);
        int start = _queue.FindLastIndex(item => item.OwnerDiscordUserId == owner) + 1;

        Dictionary<string, int> seen = new(StringComparer.Ordinal);
        for (int index = 0; index < _queue.Count; index++)
        {
            string itemOwner = _queue[index].OwnerDiscordUserId;
            int itemRound = seen.GetValueOrDefault(itemOwner);
            seen[itemOwner] = itemRound + 1;
            if (index >= start && itemRound > round)
            {
                return index;
            }
        }

        return _queue.Count;
    }

    /// <summary>Plays the first queued clip whose owner is still here, dropping any before it whose owner left.</summary>
    private void PlayNextLocked()
    {
        while (_queue.Count > 0)
        {
            QueuedClip next = _queue[0];
            _queue.RemoveAt(0);
            if (IsPresent(next.OwnerDiscordUserId))
            {
                Start(next);
                return;
            }
        }

        _playback = null;
    }

    private void Start(QueuedClip item) =>
        _playback = new RoomPlayback(item.ItemId, item.Clip, item.OwnerDiscordUserId, true, 0, timeProvider.GetUtcNow());

    private List<QueuedClip> VisibleQueue() => _queue.Where(item => IsPresent(item.OwnerDiscordUserId)).ToList();

    private bool IsPresent(string discordUserId) =>
        _connections.Values.Any(connection => connection.DiscordUserId == discordUserId);

    private bool IsHostLocked(string connectionId) =>
        _connections.TryGetValue(connectionId, out RoomConnection? connection) &&
        connection.DiscordUserId == _hostDiscordUserId;
}

/// <summary>Every open watch room on this API instance, keyed by Discord's Activity instance id.</summary>
public sealed class WatchRoomRegistry(TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<string, WatchRoom> _rooms = new(StringComparer.Ordinal);

    public WatchRoom Join(string instanceId, RoomConnection connection)
    {
        while (true)
        {
            WatchRoom room = _rooms.GetOrAdd(instanceId, id => new WatchRoom(id, timeProvider));
            if (room.TryJoin(connection))
            {
                return room;
            }

            // The last person left between the lookup and the join; that room is closed, so make a new one.
            _rooms.TryRemove(new KeyValuePair<string, WatchRoom>(instanceId, room));
        }
    }

    /// <summary>The room after the connection left, or null when it was the last one and the room is gone.</summary>
    public WatchRoom? Leave(string instanceId, string connectionId)
    {
        if (!_rooms.TryGetValue(instanceId, out WatchRoom? room))
        {
            return null;
        }

        if (!room.Leave(connectionId))
        {
            return room;
        }

        _rooms.TryRemove(new KeyValuePair<string, WatchRoom>(instanceId, room));
        return null;
    }

    public WatchRoom? Find(string instanceId) => _rooms.GetValueOrDefault(instanceId);
}
