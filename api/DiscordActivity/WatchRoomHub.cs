using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Reelshelf.Core;
using Reelshelf.Games;

namespace Reelshelf.DiscordActivity;

/// <summary>
/// The watch room's live connection (ADR-0006). A connection joins the room named in its room token, never one
/// the client picks. Every change is broadcast as a whole <see cref="RoomStateView"/> on <c>RoomState</c>.
/// Only the host can change playback, and only with clips from their own shelf.
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

    /// <summary>The host starts one of their own clips for the room, from the beginning.</summary>
    public async Task PlayClip(Guid clipId)
    {
        WatchRoom room = Room();
        RoomConnection caller = room.GetConnection(Context.ConnectionId) ?? throw NotInRoom();
        if (!room.IsHost(Context.ConnectionId) || caller.AccountId is not { } accountId)
        {
            throw new HubException("Only the host can choose what plays");
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

        await ApplyHostChange(room, room.PlayClip(Context.ConnectionId, nowPlaying));
    }

    public Task Play(double positionSeconds) => SetPlayback(true, positionSeconds);

    public Task Pause(double positionSeconds) => SetPlayback(false, positionSeconds);

    public Task Seek(double positionSeconds) => SetPlayback(null, positionSeconds);

    public async Task Stop()
    {
        WatchRoom room = Room();
        await ApplyHostChange(room, room.Stop(Context.ConnectionId));
    }

    private async Task SetPlayback(bool? playing, double positionSeconds)
    {
        WatchRoom room = Room();
        await ApplyHostChange(room, room.SetPlayback(Context.ConnectionId, playing, positionSeconds));
    }

    private async Task ApplyHostChange(WatchRoom room, bool applied)
    {
        if (!applied)
        {
            // Not the host (any more), or nothing playing: resend the room so the caller's screen catches up.
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
