using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Reelshelf.Core;
using Reelshelf.Games;

namespace Reelshelf.DiscordActivity;

/// <summary>
/// The watch room's live connection (ADR-0006). A connection joins the room named in its room token, never one
/// the client picks. Every change is broadcast as a whole <see cref="RoomStateView"/> on <c>RoomState</c>.
/// Members queue clips from their own shelf; only the host controls playback and moderates the queue.
/// </summary>
/// <remarks>The hub uses SignalR's default camelCase JSON, not the API's snake_case; its client types are hand-written.</remarks>
[Authorize(Policy = RoomTokenAuthentication.Policy)]
public sealed class WatchRoomHub(
    WatchRoomRegistry rooms,
    ActivitySignInService signIn,
    ClipsStatements clips,
    GameCategoryStatements gameCategories,
    TimeProvider timeProvider) : Hub
{
    public const string Path = "/api/activity/hub";
    public const string RoomStateMethod = "RoomState";
    public const string ReactionMethod = "Reaction";

    public override async Task OnConnectedAsync()
    {
        RoomParticipant participant = Participant();
        ActivityParticipantResponse described =
            await signIn.DescribeAsync(participant.DiscordUserId, participant.Name, participant.AvatarUrl);

        WatchRoom room = rooms.Join(participant.InstanceId, new RoomConnection(
            Context.ConnectionId,
            participant.DiscordUserId,
            described.Name,
            described.AvatarUrl,
            described.AccountId,
            described.AccountName,
            timeProvider.GetUtcNow()));

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(room.InstanceId));
        await Broadcast(room);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        string instanceId = Participant().InstanceId;
        WatchRoom? room = rooms.Leave(instanceId, Context.ConnectionId);
        if (room is not null)
        {
            await Broadcast(room);
        }
    }

    /// <summary>A member queues one of their own clips; it starts at once when nothing is playing.</summary>
    public async Task AddToQueue(Guid clipId)
    {
        WatchRoom room = Room();
        RoomConnection caller = room.GetConnection(Context.ConnectionId) ?? throw NotInRoom();
        if (caller.AccountId is not { } accountId)
        {
            throw new HubException("Link Discord to your Reelshelf account to queue clips");
        }

        ClipsStatements.ClipWithTagsRow? clip = await clips.GetClipWithTagsByIdAndOwner(clipId, accountId);
        if (clip is null || !ActivityClipService.IsPlayable(clip.VideoStatus))
        {
            throw new HubException("That clip can't be played");
        }

        string game = (await gameCategories.GetByIdsAsync([clip.GameCategoryId])).FirstOrDefault()?.Name ?? "";
        NowPlaying nowPlaying = new(
            clip.Id,
            clip.VideoId,
            string.IsNullOrWhiteSpace(clip.Title) ? "Untitled clip" : clip.Title,
            game,
            caller.AccountName ?? caller.Name,
            clip.Length ?? 0);

        switch (room.Enqueue(Context.ConnectionId, nowPlaying))
        {
            case EnqueueResult.Queued:
                await Broadcast(room);
                return;
            case EnqueueResult.Locked:
                throw new HubException("The host has locked the queue");
            case EnqueueResult.AlreadyQueued:
                throw new HubException("That clip is already in the queue");
            case EnqueueResult.Full:
                throw new HubException($"The queue is full ({WatchRoom.MaxQueueLength} clips)");
            default:
                throw new HubException("Link Discord to your Reelshelf account to queue clips");
        }
    }

    /// <summary>The host removes any queued clip; a member removes their own.</summary>
    public async Task RemoveFromQueue(Guid itemId)
    {
        WatchRoom room = Room();
        await ApplyChange(room, room.Remove(Context.ConnectionId, itemId));
    }

    public async Task MoveInQueue(Guid itemId, int toIndex)
    {
        WatchRoom room = Room();
        await ApplyChange(room, room.Move(Context.ConnectionId, itemId, toIndex));
    }

    public async Task LockQueue(bool locked)
    {
        WatchRoom room = Room();
        await ApplyChange(room, room.SetQueueLocked(Context.ConnectionId, locked));
    }

    /// <summary>
    /// Moves on to the next queued clip: the host skipping, or the host's player reaching the end of
    /// <paramref name="currentItemId"/>.
    /// </summary>
    public async Task Next(Guid? currentItemId)
    {
        WatchRoom room = Room();
        await ApplyChange(room, room.Next(Context.ConnectionId, currentItemId));
    }

    public async Task PlayNow(Guid itemId)
    {
        WatchRoom room = Room();
        await ApplyChange(room, room.PlayNow(Context.ConnectionId, itemId));
    }

    /// <summary>
    /// Sends an emoji to everyone watching. A refused reaction (nothing playing, an emoji not on offer, too
    /// many too fast) is dropped quietly rather than shown as an error.
    /// </summary>
    public async Task React(string emoji)
    {
        WatchRoom room = Room();
        if (room.React(Context.ConnectionId, emoji) is { } reaction)
        {
            await Clients.Group(GroupName(room.InstanceId)).SendAsync(ReactionMethod, reaction);
        }
    }

    public Task Play(double positionSeconds) => SetPlayback(true, positionSeconds);

    public Task Pause(double positionSeconds) => SetPlayback(false, positionSeconds);

    public Task Seek(double positionSeconds) => SetPlayback(null, positionSeconds);

    private async Task SetPlayback(bool? playing, double positionSeconds)
    {
        WatchRoom room = Room();
        await ApplyChange(room, room.SetPlayback(Context.ConnectionId, playing, positionSeconds));
    }

    private async Task ApplyChange(WatchRoom room, bool applied)
    {
        if (!applied)
        {
            // Not allowed (not the host any more, or not their clip) or out of date: resend the room so the caller's screen catches up.
            await Clients.Caller.SendAsync(RoomStateMethod, room.Snapshot());
            return;
        }

        await Broadcast(room);
    }

    private Task Broadcast(WatchRoom room) =>
        Clients.Group(GroupName(room.InstanceId)).SendAsync(RoomStateMethod, room.Snapshot());

    private WatchRoom Room() => rooms.Find(Participant().InstanceId) ?? throw NotInRoom();

    private RoomParticipant Participant() =>
        RoomTokenAuthentication.ReadParticipant(Context.User!) ?? throw new HubException("Not signed in to a room");

    private static HubException NotInRoom() => new("This room has closed. Rejoin the Activity.");

    private static string GroupName(string instanceId) => $"watch-room:{instanceId}";
}
