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

/// <summary>
/// Where playback stood at <see cref="UpdatedAt"/> (server time). While playing, clients add the time since
/// then to <see cref="PositionSeconds"/> to find where everyone should be now.
/// </summary>
public sealed record RoomPlayback(NowPlaying Clip, bool Playing, double PositionSeconds, DateTimeOffset UpdatedAt);

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
    DateTimeOffset ServerTime);

/// <summary>
/// A watch room: everyone connected to one Activity instance, its host and what is playing. Held in memory
/// only (ADR-0006). Every method takes the room's lock, so the hub can call it from any connection.
/// </summary>
public sealed class WatchRoom(string instanceId, TimeProvider timeProvider)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, RoomConnection> _connections = new(StringComparer.Ordinal);
    private string? _hostDiscordUserId;
    private RoomPlayback? _playback;
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
            // member who has been here longest. With only guests left there is no host.
            if (left.DiscordUserId == _hostDiscordUserId &&
                _connections.Values.All(remaining => remaining.DiscordUserId != left.DiscordUserId))
            {
                _hostDiscordUserId = _connections.Values
                    .Where(remaining => remaining.IsMember)
                    .OrderBy(remaining => remaining.ConnectedAt)
                    .Select(remaining => remaining.DiscordUserId)
                    .FirstOrDefault();
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
            return _connections.TryGetValue(connectionId, out RoomConnection? connection) &&
                   connection.DiscordUserId == _hostDiscordUserId;
        }
    }

    /// <summary>The host starts a clip from the beginning. False when the caller is not the host.</summary>
    public bool PlayClip(string connectionId, NowPlaying clip)
    {
        lock (_gate)
        {
            if (!IsHostLocked(connectionId))
            {
                return false;
            }

            _playback = new RoomPlayback(clip, true, 0, timeProvider.GetUtcNow());
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

    public bool Stop(string connectionId)
    {
        lock (_gate)
        {
            if (!IsHostLocked(connectionId))
            {
                return false;
            }

            _playback = null;
            _version++;
            return true;
        }
    }

    public RoomStateView Snapshot()
    {
        lock (_gate)
        {
            // One row per person, however many connections they have, in the order they arrived.
            List<RoomParticipantView> participants = _connections.Values
                .GroupBy(connection => connection.DiscordUserId)
                .Select(group => group.OrderBy(connection => connection.ConnectedAt).First())
                .OrderBy(connection => connection.ConnectedAt)
                .Select(connection => new RoomParticipantView(
                    connection.DiscordUserId,
                    connection.Name,
                    connection.AvatarUrl,
                    connection.IsMember,
                    connection.DiscordUserId == _hostDiscordUserId))
                .ToList();

            return new RoomStateView(_version, _hostDiscordUserId, participants, _playback, timeProvider.GetUtcNow());
        }
    }

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
