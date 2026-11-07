using System.Text.Json;
using System.Text.Json.Serialization;
using Reelshelf.Core;
using Reelshelf.Video;

namespace Reelshelf.Discord.DiscordActivity;

/// <summary>
/// One hub connection in a room. A person with the Activity open twice (desktop and phone) has two.
/// <see cref="ServerId"/> is the API instance holding it (<see cref="WatchRoomServer"/>), and
/// <see cref="DisconnectedAt"/> is set once it dropped: the room keeps it for <see cref="WatchRoom.ReconnectGrace"/>
/// so a reconnect (a deploy, a network blip) finds the person still in it.
/// </summary>
public sealed record RoomConnection(
    string ConnectionId,
    string DiscordUserId,
    string Name,
    string? AvatarUrl,
    Guid? AccountId,
    string? AccountName,
    DateTimeOffset ConnectedAt)
{
    [JsonIgnore]
    public bool IsMember => AccountId is not null;

    public string ServerId { get; init; } = "";

    public DateTimeOffset? DisconnectedAt { get; init; }
}

/// <summary>
/// The clip on screen. Guests see only this much of it: title, game and whose shelf it came from, plus the
/// video id and media the player streams.
/// </summary>
public sealed record NowPlaying(
    Guid ClipId,
    Guid VideoId,
    string Title,
    string Game,
    string OwnerName,
    int DurationSeconds,
    ActivityMedia Media);

/// <summary>
/// Where the Activity finds a clip's video. Discord's CSP lets the Activity load only through the URL Mappings on the
/// Discord application, one per media origin, so it builds the URLs itself from these
/// (<c>frontend/src/shared/utils/clipMediaUrls.ts</c>): the <paramref name="Host"/> picks the mapping and the file
/// names, and <paramref name="Path"/> is the video's folder under it.
/// </summary>
/// <param name="Host">The clip's <c>video_host</c>.</param>
/// <param name="Path">The video's folder relative to its host's media origin (<see cref="ClipMediaUrls.MediaPath"/>).</param>
/// <param name="Type">
/// The stream's MIME type, with codecs where known, for the player to check before playing. Null until the clip is
/// ready, when it has no stream yet.
/// </param>
/// <param name="HasThumbnail">Whether the clip's <c>thumbnail.jpg</c> exists yet (<see cref="ClipMediaUrls.Thumbnail"/>).</param>
public sealed record ActivityMedia(string Host, string Path, string? Type, bool HasThumbnail)
{
    public static ActivityMedia For(ClipMediaUrls mediaUrls, ClipVideoRef video) =>
        new(video.VideoHost, mediaUrls.MediaPath(video), mediaUrls.Sources(video).FirstOrDefault()?.Type,
            mediaUrls.Thumbnail(video) is not null);
}

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

/// <summary>
/// An emoji someone sent while a clip played. It is broadcast once and never kept: it is not part of
/// <see cref="RoomStateView"/>.
/// </summary>
public sealed record RoomReaction(Guid ItemId, string DiscordUserId, string Name, string Emoji);

public enum EnqueueResult
{
    Queued,
    NotAMember,
    Locked,
    AlreadyQueued,
    Full
}


/// <summary>What a room keeps between changes: the JSON stored for it in Redis (<see cref="IWatchRoomStore"/>).</summary>
internal sealed class WatchRoomState
{
    public List<RoomConnection> Connections { get; set; } = [];
    public List<QueuedClip> Queue { get; set; } = [];
    public string? HostDiscordUserId { get; set; }
    public RoomPlayback? Playback { get; set; }
    public bool QueueLocked { get; set; }
    public long Version { get; set; }
}

/// <summary>
/// A watch room: everyone connected to one Activity instance, its host, what is playing and what is queued. It is
/// the room's rules over its stored state: <see cref="WatchRoomRegistry"/> loads one from Redis for each change,
/// applies the change and saves it only if no other change came first, so any API instance can serve any room and a
/// deploy loses nothing.
/// </summary>
/// <remarks>
/// A queued clip is shown to the room only while its owner is in it: clips of an owner who has left are hidden
/// from the queue and dropped when their turn comes, and come back if the owner reconnects first. A clip that
/// is already playing plays on. When no member is left, the room has nothing to play and its queue empties.
/// Someone whose connection drops is still in the room for <see cref="ReconnectGrace"/>, keeping the host role and
/// their queued clips, so everyone reconnecting after a deploy finds the room as they left it.
/// </remarks>
public sealed class WatchRoom
{
    public const int MaxQueueLength = 50;

    /// <summary>The reactions on offer; the Activity's reaction bar shows the same set.</summary>
    public static readonly IReadOnlyList<string> ReactionEmoji = ["😂", "🔥", "😮", "👏", "💀", "❤️"];

    /// <summary>Each person may send this many reactions per <see cref="ReactionWindow"/>, so a held key can't flood the room.</summary>
    public const int ReactionsPerWindow = 6;

    public static readonly TimeSpan ReactionWindow = TimeSpan.FromSeconds(3);

    /// <summary>
    /// How long past a playing clip's expected end the room waits for the host's player to report it before moving
    /// on by itself (<see cref="AdvanceIfOverdue"/>), for a host whose stream failed or whose browser blocked play.
    /// </summary>
    public static readonly TimeSpan EndGrace = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long a dropped connection still counts as in the room. A deploy closes every connection on the old
    /// container and the Activity reconnects within a few seconds (its retries wait 1, 2, 4 and 8 s), so this covers
    /// that with room to spare; a person who really left goes once it is up.
    /// </summary>
    public static readonly TimeSpan ReconnectGrace = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly TimeProvider _timeProvider;
    private readonly WatchRoomState _state;

    public WatchRoom(string instanceId, TimeProvider timeProvider) : this(instanceId, timeProvider, new WatchRoomState())
    {
    }

    private WatchRoom(string instanceId, TimeProvider timeProvider, WatchRoomState state)
    {
        InstanceId = instanceId;
        _timeProvider = timeProvider;
        _state = state;
    }

    public string InstanceId { get; }

    /// <summary>True once nobody is left, not even within their <see cref="ReconnectGrace"/>; the room is then deleted.</summary>
    internal bool IsEmpty => _state.Connections.Count == 0;

    internal long Version => _state.Version;

    /// <summary>The API instances holding connections that haven't dropped, so the registry can check they are still running.</summary>
    internal IEnumerable<string> LiveServerIds => _state.Connections
        .Where(connection => connection.DisconnectedAt is null)
        .Select(connection => connection.ServerId)
        .Distinct(StringComparer.Ordinal);

    internal string ToJson() => JsonSerializer.Serialize(_state, JsonOptions);

    internal static WatchRoom FromJson(string instanceId, string json, TimeProvider timeProvider) =>
        new(instanceId, timeProvider,
            JsonSerializer.Deserialize<WatchRoomState>(json, JsonOptions) ?? new WatchRoomState());

    /// <summary>
    /// Adds a connection. Someone coming back keeps their place: their earliest arrival still decides who hosts next,
    /// and the connections they dropped are gone.
    /// </summary>
    internal void Join(RoomConnection connection)
    {
        DateTimeOffset connectedAt = _state.Connections
            .Where(existing => existing.DiscordUserId == connection.DiscordUserId)
            .Select(existing => existing.ConnectedAt)
            .Append(connection.ConnectedAt)
            .Min();

        _state.Connections.RemoveAll(existing =>
            existing.ConnectionId == connection.ConnectionId ||
            (existing.DiscordUserId == connection.DiscordUserId && existing.DisconnectedAt is not null));
        _state.Connections.Add(connection with { ConnectedAt = connectedAt, DisconnectedAt = null });
        _state.Version++;
        if (_state.HostDiscordUserId is null && connection.IsMember)
        {
            _state.HostDiscordUserId = connection.DiscordUserId;
        }
    }

    /// <summary>
    /// Marks a connection dropped. Nothing the room shows changes yet: the person stays for
    /// <see cref="ReconnectGrace"/>, and <see cref="Prune"/> takes them out once it is up.
    /// </summary>
    internal void Leave(string connectionId)
    {
        int index = _state.Connections.FindIndex(connection => connection.ConnectionId == connectionId);
        if (index >= 0 && _state.Connections[index].DisconnectedAt is null)
        {
            _state.Connections[index] = _state.Connections[index] with { DisconnectedAt = _timeProvider.GetUtcNow() };
        }
    }

    /// <summary>
    /// Starts the grace of connections whose API instance stopped without saying so (<paramref name="serverAlive"/>
    /// false), and removes connections that dropped <see cref="ReconnectGrace"/> ago or more. When a person goes the
    /// host role passes to the member who has been here longest; with only guests left there is no host, and no clips.
    /// </summary>
    internal void Prune(Func<string, bool> serverAlive)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        for (int index = 0; index < _state.Connections.Count; index++)
        {
            RoomConnection connection = _state.Connections[index];
            if (connection.DisconnectedAt is null && !serverAlive(connection.ServerId))
            {
                _state.Connections[index] = connection with { DisconnectedAt = now };
            }
        }

        if (_state.Connections.RemoveAll(connection => now - connection.DisconnectedAt >= ReconnectGrace) == 0)
        {
            return;
        }

        _state.Version++;
        if (_state.HostDiscordUserId is { } host && !IsPresent(host))
        {
            _state.HostDiscordUserId = _state.Connections
                .Where(remaining => remaining.IsMember)
                .OrderBy(remaining => remaining.ConnectedAt)
                .Select(remaining => remaining.DiscordUserId)
                .FirstOrDefault();
        }

        if (_state.HostDiscordUserId is null)
        {
            _state.Playback = null;
            _state.Queue.Clear();
            _state.QueueLocked = false;
        }
    }

    public RoomConnection? GetConnection(string connectionId) =>
        _state.Connections.FirstOrDefault(connection => connection.ConnectionId == connectionId);

    public bool IsHost(string connectionId) =>
        GetConnection(connectionId) is { } connection && connection.DiscordUserId == _state.HostDiscordUserId;

    /// <summary>
    /// A member queues one of their own clips. It goes in round-robin by who queued it: a member's first clip
    /// waits behind everyone else's first, their second behind everyone's second, and so on. When nothing is
    /// playing it starts at once. While the queue is locked only the host can add.
    /// </summary>
    public EnqueueResult Enqueue(string connectionId, NowPlaying clip)
    {
        if (GetConnection(connectionId) is not { IsMember: true } caller)
        {
            return EnqueueResult.NotAMember;
        }

        if (_state.QueueLocked && caller.DiscordUserId != _state.HostDiscordUserId)
        {
            return EnqueueResult.Locked;
        }

        if (_state.Queue.Any(item => item.Clip.ClipId == clip.ClipId))
        {
            return EnqueueResult.AlreadyQueued;
        }

        if (_state.Queue.Count >= MaxQueueLength)
        {
            return EnqueueResult.Full;
        }

        QueuedClip queued = new(Guid.NewGuid(), clip, caller.DiscordUserId);
        _state.Queue.Insert(RoundRobinIndex(caller.DiscordUserId), queued);
        if (_state.Playback is null)
        {
            PlayNext();
        }

        _state.Version++;
        return EnqueueResult.Queued;
    }

    /// <summary>The host can remove any queued clip; a member can remove their own.</summary>
    public bool Remove(string connectionId, Guid itemId)
    {
        int index = _state.Queue.FindIndex(item => item.ItemId == itemId);
        if (index < 0 || GetConnection(connectionId) is not { } caller ||
            (caller.DiscordUserId != _state.HostDiscordUserId &&
             caller.DiscordUserId != _state.Queue[index].OwnerDiscordUserId))
        {
            return false;
        }

        _state.Queue.RemoveAt(index);
        _state.Version++;
        return true;
    }

    /// <summary>The host moves a queued clip to <paramref name="toIndex"/> among the clips the room can see.</summary>
    public bool Move(string connectionId, Guid itemId, int toIndex)
    {
        List<QueuedClip> visible = VisibleQueue();
        QueuedClip? moving = visible.FirstOrDefault(item => item.ItemId == itemId);
        if (!IsHost(connectionId) || moving is null)
        {
            return false;
        }

        // Place it before the visible clip now at that position, or after the last one; hidden clips of
        // absent owners keep their places.
        visible.Remove(moving);
        _state.Queue.Remove(moving);
        int target = Math.Clamp(toIndex, 0, visible.Count);
        int at = target < visible.Count ? _state.Queue.IndexOf(visible[target]) : _state.Queue.Count;
        _state.Queue.Insert(at, moving);
        _state.Version++;
        return true;
    }

    /// <summary>The host locks the queue so only they can add, for a showcase.</summary>
    public bool SetQueueLocked(string connectionId, bool locked)
    {
        if (!IsHost(connectionId))
        {
            return false;
        }

        _state.QueueLocked = locked;
        _state.Version++;
        return true;
    }

    /// <summary>
    /// The host moves on from <paramref name="currentItemId"/> to the next queued clip, or stops when the queue is
    /// empty. It only moves on from the clip that is playing, so a clip ending (or skipped) on two of the host's
    /// devices, or ended by the room's own clock first, moves on once.
    /// </summary>
    public bool Next(string connectionId, Guid currentItemId)
    {
        if (!IsHost(connectionId) || _state.Playback?.ItemId != currentItemId)
        {
            return false;
        }

        PlayNext();
        _state.Version++;
        return true;
    }

    /// <summary>The host plays a queued clip now, taking it out of the queue.</summary>
    public bool PlayNow(string connectionId, Guid itemId)
    {
        QueuedClip? item = VisibleQueue().FirstOrDefault(queued => queued.ItemId == itemId);
        if (!IsHost(connectionId) || item is null)
        {
            return false;
        }

        _state.Queue.Remove(item);
        Start(item);
        _state.Version++;
        return true;
    }

    /// <summary>
    /// The host plays, pauses or seeks the current clip. <paramref name="playing"/> null keeps the current
    /// play state (a seek). False when the caller is not the host or nothing is on.
    /// </summary>
    public bool SetPlayback(string connectionId, bool? playing, double positionSeconds)
    {
        if (!IsHost(connectionId) || _state.Playback is not { } playback || !double.IsFinite(positionSeconds))
        {
            return false;
        }

        double position = Math.Max(0, positionSeconds);
        if (playback.Clip.DurationSeconds > 0)
        {
            position = Math.Min(position, playback.Clip.DurationSeconds);
        }

        _state.Playback = playback with
        {
            Playing = playing ?? playback.Playing,
            PositionSeconds = position,
            UpdatedAt = _timeProvider.GetUtcNow()
        };
        _state.Version++;
        return true;
    }

    /// <summary>
    /// The room's own clock: moves on to the next clip once the playing clip is <see cref="EndGrace"/> past its
    /// expected end (where it was at <see cref="RoomPlayback.UpdatedAt"/> plus the time left of its length), so the
    /// room never waits for ever on a host whose player can't play. A paused clip, or one of unknown length, never
    /// moves on by itself.
    /// </summary>
    public bool AdvanceIfOverdue()
    {
        if (_state.Playback is not { Playing: true } playback || playback.Clip.DurationSeconds <= 0)
        {
            return false;
        }

        double left = Math.Max(0, playback.Clip.DurationSeconds - playback.PositionSeconds);
        if (_timeProvider.GetUtcNow() < playback.UpdatedAt + TimeSpan.FromSeconds(left) + EndGrace)
        {
            return false;
        }

        PlayNext();
        _state.Version++;
        return true;
    }

    /// <summary>
    /// Anyone in the room, guests included, reacts to the clip that is playing. Null when nothing is playing,
    /// the emoji isn't one on offer, or the caller isn't in the room. How often a person may react is
    /// <see cref="ReactionLimiter"/>'s to say, since reactions are never stored.
    /// </summary>
    public RoomReaction? React(string connectionId, string emoji)
    {
        if (_state.Playback is not { } playback || !ReactionEmoji.Contains(emoji) ||
            GetConnection(connectionId) is not { } caller)
        {
            return null;
        }

        return new RoomReaction(playback.ItemId, caller.DiscordUserId, caller.Name, emoji);
    }

    public RoomStateView Snapshot()
    {
        // One row per person, however many connections they have, in the order they arrived.
        List<RoomParticipantView> participants = _state.Connections
            .GroupBy(connection => connection.DiscordUserId)
            .Select(group => group.OrderBy(connection => connection.ConnectedAt).First())
            .OrderBy(connection => connection.ConnectedAt)
            .Select(connection => new RoomParticipantView(
                connection.DiscordUserId,
                connection.Name,
                connection.AvatarUrl,
                connection.IsMember,
                connection.DiscordUserId == _state.HostDiscordUserId))
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
            _state.Version,
            _state.HostDiscordUserId,
            participants,
            _state.Playback,
            queue,
            _state.QueueLocked,
            _timeProvider.GetUtcNow());
    }

    /// <summary>
    /// Where a new clip from <paramref name="owner"/> goes. It is the owner's round <c>k</c>, where <c>k</c>
    /// is how many of their clips are already queued. It goes after the owner's own queued clips, before the
    /// first clip from a later round. The host's manual moves are kept, since nothing already queued moves.
    /// </summary>
    private int RoundRobinIndex(string owner)
    {
        List<QueuedClip> queue = _state.Queue;
        int round = queue.Count(item => item.OwnerDiscordUserId == owner);
        int start = queue.FindLastIndex(item => item.OwnerDiscordUserId == owner) + 1;

        Dictionary<string, int> seen = new(StringComparer.Ordinal);
        for (int index = 0; index < queue.Count; index++)
        {
            string itemOwner = queue[index].OwnerDiscordUserId;
            int itemRound = seen.GetValueOrDefault(itemOwner);
            seen[itemOwner] = itemRound + 1;
            if (index >= start && itemRound > round)
            {
                return index;
            }
        }

        return queue.Count;
    }

    /// <summary>Plays the first queued clip whose owner is still here, dropping any before it whose owner left.</summary>
    private void PlayNext()
    {
        while (_state.Queue.Count > 0)
        {
            QueuedClip next = _state.Queue[0];
            _state.Queue.RemoveAt(0);
            if (IsPresent(next.OwnerDiscordUserId))
            {
                Start(next);
                return;
            }
        }

        _state.Playback = null;
    }

    private void Start(QueuedClip item) =>
        _state.Playback = new RoomPlayback(item.ItemId, item.Clip, item.OwnerDiscordUserId, true, 0,
            _timeProvider.GetUtcNow());

    private List<QueuedClip> VisibleQueue() => _state.Queue.Where(item => IsPresent(item.OwnerDiscordUserId)).ToList();

    /// <summary>Whether the person has a connection in the room, counting one that dropped within its grace.</summary>
    private bool IsPresent(string discordUserId) =>
        _state.Connections.Any(connection => connection.DiscordUserId == discordUserId);
}
