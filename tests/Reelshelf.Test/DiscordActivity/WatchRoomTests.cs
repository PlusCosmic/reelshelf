using Reelshelf.Discord.DiscordActivity;
using Xunit;

namespace Reelshelf.Test.DiscordActivity;

/// <summary>
/// The watch room's rules: who hosts, who may change playback and the queue, how the queue is ordered, how playback
/// is recorded, who may react, and how a dropped connection keeps its place for a while. The registry stores it and
/// the hub is a thin layer over both (<see cref="WatchRoomRegistryTests"/>).
/// </summary>
public class WatchRoomTests
{
    private const string Instance = "i-1";
    private static readonly NowPlaying Clip = NewClip("Triple");

    private readonly TestClock _clock = new();
    private readonly WatchRoom _room;

    public WatchRoomTests()
    {
        _room = new WatchRoom(Instance, _clock);
    }

    [Fact]
    public void TheFirstMemberHosts_GuestsNever()
    {
        Join(Guest("g1", "guest"));
        Assert.Null(_room.Snapshot().HostDiscordUserId);

        WatchRoom room = Join(Member("c1", "alice"));
        Join(Member("c2", "bob"));

        RoomStateView state = room.Snapshot();
        Assert.Equal("alice", state.HostDiscordUserId);
        Assert.Equal(["guest", "alice", "bob"], state.Participants.Select(p => p.DiscordUserId));
        Assert.True(state.Participants.Single(p => p.DiscordUserId == "alice").IsHost);
        Assert.False(state.Participants.Single(p => p.DiscordUserId == "guest").IsMember);
    }

    [Fact]
    public void WhenTheHostLeaves_TheLongestConnectedMemberTakesOver()
    {
        WatchRoom room = Join(Member("c1", "alice"));
        Join(Guest("g1", "guest"));
        Join(Member("c2", "bob"));
        Join(Member("c3", "carol"));

        LeaveForGood("c1");
        Assert.Equal("bob", room.Snapshot().HostDiscordUserId);

        LeaveForGood("c2");
        LeaveForGood("c3");
        Assert.Null(room.Snapshot().HostDiscordUserId);
    }

    [Fact]
    public void TheHostKeepsTheRole_WhileAnyOfTheirConnectionsRemain()
    {
        WatchRoom room = Join(Member("desktop", "alice"));
        Join(Member("c2", "bob"));
        Join(Member("phone", "alice"));

        LeaveForGood("desktop");

        RoomStateView state = room.Snapshot();
        Assert.Equal("alice", state.HostDiscordUserId);
        Assert.Equal(2, state.Participants.Count);
        Assert.True(room.IsHost("phone"));
    }

    [Fact]
    public void OnlyTheHostChangesPlayback()
    {
        WatchRoom room = Join(Member("c1", "alice"));
        Join(Member("c2", "bob"));
        Join(Guest("g1", "guest"));

        Assert.Equal(EnqueueResult.NotAMember, room.Enqueue("g1", Clip));
        Assert.Equal(EnqueueResult.Queued, room.Enqueue("c2", Clip));
        Assert.Equal(Clip, room.Snapshot().Playback!.Clip);

        Assert.False(room.SetPlayback("c2", false, 5));
        Assert.False(room.SetPlayback("g1", false, 5));
        Assert.False(room.Next("c2", room.Snapshot().Playback!.ItemId));
        Assert.False(room.SetQueueLocked("c2", true));
        Assert.True(room.SetPlayback("c1", false, 5));
        Assert.False(room.Snapshot().Playback!.Playing);
    }

    [Fact]
    public void PlaybackRecordsPositionAndServerTime_ClampedToTheClip()
    {
        WatchRoom room = Join(Member("c1", "alice"));
        Assert.False(room.SetPlayback("c1", true, 3));

        room.Enqueue("c1", Clip);
        RoomPlayback started = room.Snapshot().Playback!;
        Assert.True(started.Playing);
        Assert.Equal(0, started.PositionSeconds);
        Assert.Equal(_clock.GetUtcNow(), started.UpdatedAt);

        _clock.Advance(TimeSpan.FromSeconds(4));
        room.SetPlayback("c1", false, 4.2);
        RoomPlayback paused = room.Snapshot().Playback!;
        Assert.False(paused.Playing);
        Assert.Equal(4.2, paused.PositionSeconds);
        Assert.Equal(_clock.GetUtcNow(), paused.UpdatedAt);

        room.SetPlayback("c1", null, 99);
        Assert.Equal(30, room.Snapshot().Playback!.PositionSeconds);
        Assert.False(room.Snapshot().Playback!.Playing);

        room.SetPlayback("c1", null, -5);
        Assert.Equal(0, room.Snapshot().Playback!.PositionSeconds);
        Assert.False(room.SetPlayback("c1", true, double.NaN));

        room.Next("c1", room.Snapshot().Playback!.ItemId);
        Assert.Null(room.Snapshot().Playback);
    }

    [Fact]
    public void EveryChangeRaisesTheVersion_RefusedOnesDoNot()
    {
        WatchRoom room = Join(Member("c1", "alice"));
        Join(Guest("g1", "guest"));
        long afterJoins = room.Snapshot().Version;

        room.Enqueue("g1", Clip);
        Assert.Equal(afterJoins, room.Snapshot().Version);

        room.Enqueue("c1", Clip);
        room.SetPlayback("c1", false, 1);
        Assert.Equal(afterJoins + 2, room.Snapshot().Version);
    }

    [Fact]
    public void TheQueueTakesTurnsByWhoQueued()
    {
        WatchRoom room = Join(Member("c1", "alice"));
        Join(Member("c2", "bob"));
        Join(Member("c3", "carol"));

        room.Enqueue("c1", NewClip("a0")); // Plays at once.
        room.Enqueue("c1", NewClip("a1"));
        room.Enqueue("c1", NewClip("a2"));
        room.Enqueue("c1", NewClip("a3"));
        room.Enqueue("c2", NewClip("b1"));
        room.Enqueue("c2", NewClip("b2"));
        room.Enqueue("c3", NewClip("c1"));

        Assert.Equal("a0", room.Snapshot().Playback!.Clip.Title);
        Assert.Equal(["a1", "b1", "c1", "a2", "b2", "a3"], QueueTitles(room));
    }

    [Fact]
    public void ANewClipNeverJumpsAheadOfTheOwnersEarlierOnes_AfterTheHostReorders()
    {
        WatchRoom room = Join(Member("c1", "alice"));
        Join(Member("c2", "bob"));
        room.Enqueue("c1", NewClip("playing"));
        room.Enqueue("c1", NewClip("a1"));
        room.Enqueue("c2", NewClip("b1"));
        room.Enqueue("c2", NewClip("b2"));
        Assert.Equal(["a1", "b1", "b2"], QueueTitles(room));

        // The host sends Bob's first clip to the back, behind his second.
        Assert.True(room.Move("c1", ItemId(room, "b1"), 2));
        Assert.Equal(["a1", "b2", "b1"], QueueTitles(room));

        room.Enqueue("c2", NewClip("b3"));
        Assert.Equal(["a1", "b2", "b1", "b3"], QueueTitles(room));
    }

    [Fact]
    public void TheHostModerates_MembersRemoveOnlyTheirOwn()
    {
        WatchRoom room = Join(Member("c1", "alice"));
        Join(Member("c2", "bob"));
        Join(Member("c3", "carol"));
        room.Enqueue("c1", NewClip("playing"));
        room.Enqueue("c2", NewClip("b1"));
        room.Enqueue("c3", NewClip("c1"));
        room.Enqueue("c2", NewClip("b2"));

        Assert.False(room.Remove("c3", ItemId(room, "b1")));
        Assert.False(room.Move("c2", ItemId(room, "b2"), 0));
        Assert.False(room.PlayNow("c2", ItemId(room, "b2")));
        Assert.True(room.Remove("c2", ItemId(room, "b1")));
        Assert.True(room.Remove("c1", ItemId(room, "c1")));
        Assert.Equal(["b2"], QueueTitles(room));

        Assert.True(room.PlayNow("c1", ItemId(room, "b2")));
        Assert.Equal("b2", room.Snapshot().Playback!.Clip.Title);
        Assert.Empty(room.Snapshot().Queue);
    }

    [Fact]
    public void ALockedQueueTakesClipsOnlyFromTheHost()
    {
        WatchRoom room = Join(Member("c1", "alice"));
        Join(Member("c2", "bob"));
        Assert.True(room.SetQueueLocked("c1", true));
        Assert.True(room.Snapshot().QueueLocked);

        Assert.Equal(EnqueueResult.Locked, room.Enqueue("c2", NewClip("b1")));
        Assert.Equal(EnqueueResult.Queued, room.Enqueue("c1", NewClip("a1")));

        room.SetQueueLocked("c1", false);
        Assert.Equal(EnqueueResult.Queued, room.Enqueue("c2", NewClip("b1")));
    }

    [Fact]
    public void AClipIsQueuedOnce_AndTheQueueHasALimit()
    {
        WatchRoom room = Join(Member("c1", "alice"));
        NowPlaying again = NewClip("again");
        room.Enqueue("c1", NewClip("playing"));
        Assert.Equal(EnqueueResult.Queued, room.Enqueue("c1", again));
        Assert.Equal(EnqueueResult.AlreadyQueued, room.Enqueue("c1", again));

        for (int i = 1; i < WatchRoom.MaxQueueLength; i++)
        {
            Assert.Equal(EnqueueResult.Queued, room.Enqueue("c1", NewClip($"clip {i}")));
        }

        Assert.Equal(EnqueueResult.Full, room.Enqueue("c1", NewClip("one too many")));
    }

    [Fact]
    public void NextMovesOnOnce_FromTheClipThatEnded()
    {
        WatchRoom room = Join(Member("c1", "alice"));
        room.Enqueue("c1", NewClip("first"));
        room.Enqueue("c1", NewClip("second"));
        Guid first = room.Snapshot().Playback!.ItemId;

        Assert.True(room.Next("c1", first));
        Assert.Equal("second", room.Snapshot().Playback!.Clip.Title);
        Assert.False(room.Next("c1", first));
        Assert.Equal("second", room.Snapshot().Playback!.Clip.Title);

        Assert.True(room.Next("c1", room.Snapshot().Playback!.ItemId));
        Assert.Null(room.Snapshot().Playback);
    }

    [Fact]
    public void TwoOfTheHostsDevicesSkippingTheSameClip_MoveOnOnce()
    {
        WatchRoom room = Join(Member("desktop", "alice"));
        Join(Member("phone", "alice"));
        room.Enqueue("desktop", NewClip("first"));
        room.Enqueue("desktop", NewClip("second"));
        room.Enqueue("desktop", NewClip("third"));
        Guid first = room.Snapshot().Playback!.ItemId;

        Assert.True(room.Next("desktop", first));
        Assert.False(room.Next("phone", first));
        Assert.Equal("second", room.Snapshot().Playback!.Clip.Title);
        Assert.Equal(["third"], QueueTitles(room));
    }

    [Fact]
    public void ThePlayingClipMovesOnByItself_AGracePeriodAfterItsExpectedEnd()
    {
        WatchRoom room = Join(Member("c1", "alice"));
        room.Enqueue("c1", NewClip("first"));
        room.Enqueue("c1", NewClip("second"));
        Guid first = room.Snapshot().Playback!.ItemId;

        // 30 s long, sought to 10 s at once: it ends 20 s from now, and the room waits the grace beyond that.
        room.SetPlayback("c1", null, 10);
        _clock.Advance(TimeSpan.FromSeconds(20) + WatchRoom.EndGrace - TimeSpan.FromMilliseconds(1));
        Assert.False(room.AdvanceIfOverdue());

        _clock.Advance(TimeSpan.FromMilliseconds(1));
        long version = room.Snapshot().Version;
        Assert.True(room.AdvanceIfOverdue());
        RoomStateView state = room.Snapshot();
        Assert.Equal("second", state.Playback!.Clip.Title);
        Assert.Equal(version + 1, state.Version);

        // The host's player reporting the end late moves nothing more.
        Assert.False(room.Next("c1", first));
        Assert.Equal("second", room.Snapshot().Playback!.Clip.Title);

        _clock.Advance(TimeSpan.FromSeconds(30) + WatchRoom.EndGrace);
        Assert.True(room.AdvanceIfOverdue());
        Assert.Null(room.Snapshot().Playback);
        Assert.False(room.AdvanceIfOverdue());
    }

    [Fact]
    public void APausedClip_OrOneOfUnknownLength_NeverMovesOnByItself()
    {
        WatchRoom room = Join(Member("c1", "alice"));
        room.Enqueue("c1", NewClip("first"));
        room.Enqueue("c1", NewClip("second"));

        _clock.Advance(TimeSpan.FromSeconds(5));
        room.SetPlayback("c1", false, 5);
        _clock.Advance(TimeSpan.FromMinutes(10));
        Assert.False(room.AdvanceIfOverdue());
        Assert.Equal("first", room.Snapshot().Playback!.Clip.Title);

        // Played again from 5 s, it has 25 s left from now.
        room.SetPlayback("c1", true, 5);
        _clock.Advance(TimeSpan.FromSeconds(25));
        Assert.False(room.AdvanceIfOverdue());
        _clock.Advance(WatchRoom.EndGrace);
        Assert.True(room.AdvanceIfOverdue());
        Assert.Equal("second", room.Snapshot().Playback!.Clip.Title);

        room.Enqueue("c1", NewClip("no length") with { DurationSeconds = 0 });
        room.Next("c1", room.Snapshot().Playback!.ItemId);
        Assert.Equal("no length", room.Snapshot().Playback!.Clip.Title);
        _clock.Advance(TimeSpan.FromHours(1));
        Assert.False(room.AdvanceIfOverdue());
    }

    [Fact]
    public void AnAbsentOwnersClipsAreHiddenAndSkipped_UntilTheyReturn()
    {
        WatchRoom room = Join(Member("c1", "alice"));
        Join(Member("c2", "bob"));
        room.Enqueue("c1", NewClip("playing"));
        room.Enqueue("c2", NewClip("b1"));
        room.Enqueue("c1", NewClip("a1"));
        Assert.Equal(["b1", "a1"], QueueTitles(room));

        LeaveForGood("c2");
        Assert.Equal(["a1"], QueueTitles(room));
        Assert.Equal("playing", room.Snapshot().Playback!.Clip.Title);

        // Bob comes back before his turn: his clip is still there.
        Join(Member("c2b", "bob"));
        Assert.Equal(["b1", "a1"], QueueTitles(room));

        // He leaves again; when his turn comes his clip is dropped.
        LeaveForGood("c2b");
        room.Next("c1", room.Snapshot().Playback!.ItemId);
        Assert.Equal("a1", room.Snapshot().Playback!.Clip.Title);
        Join(Member("c2c", "bob"));
        Assert.Empty(room.Snapshot().Queue);
    }

    [Fact]
    public void WhenOnlyGuestsAreLeft_NothingPlaysAndTheQueueEmpties()
    {
        WatchRoom room = Join(Member("c1", "alice"));
        Join(Guest("g1", "guest"));
        room.Enqueue("c1", NewClip("playing"));
        room.Enqueue("c1", NewClip("next"));
        room.SetQueueLocked("c1", true);

        LeaveForGood("c1");
        RoomStateView state = room.Snapshot();
        Assert.Null(state.Playback);
        Assert.Empty(state.Queue);
        Assert.False(state.QueueLocked);

        // Alice coming back finds an empty room, not her old queue.
        Join(Member("c1b", "alice"));
        Assert.Empty(room.Snapshot().Queue);
    }

    [Fact]
    public void AnyoneReactsToThePlayingClip_WithAnEmojiOnOffer()
    {
        WatchRoom room = Join(Member("c1", "alice"));
        Join(Guest("g1", "guest"));
        Assert.Null(room.React("g1", "🔥"));

        room.Enqueue("c1", Clip);
        long version = room.Snapshot().Version;
        RoomReaction reaction = room.React("g1", "🔥")!;

        Assert.Equal(room.Snapshot().Playback!.ItemId, reaction.ItemId);
        Assert.Equal("guest", reaction.DiscordUserId);
        Assert.Equal("🔥", reaction.Emoji);
        Assert.Null(room.React("g1", "🍕"));
        Assert.Null(room.React("nobody", "🔥"));
        Assert.Equal(version, room.Snapshot().Version);
    }

    [Fact]
    public void ADroppedConnectionKeepsTheHostRoleAndTheQueue_ForTheReconnectGrace()
    {
        WatchRoom room = Join(Member("c1", "alice"));
        Join(Member("c2", "bob"));
        room.Enqueue("c1", NewClip("playing"));
        room.Enqueue("c1", NewClip("a1"));
        room.Enqueue("c2", NewClip("b1"));
        long version = room.Snapshot().Version;

        // A deploy drops everyone at once.
        room.Leave("c1");
        room.Leave("c2");
        _clock.Advance(WatchRoom.ReconnectGrace - TimeSpan.FromSeconds(1));
        room.Prune(_ => true);
        Assert.Equal(version, room.Snapshot().Version);

        // Bob is back first, but Alice still hosts and her clips still wait.
        Join(Member("c2-again", "bob"));
        Join(Member("c1-again", "alice"));
        RoomStateView state = room.Snapshot();
        Assert.Equal("alice", state.HostDiscordUserId);
        Assert.Equal(["alice", "bob"], state.Participants.Select(p => p.DiscordUserId));
        Assert.Equal(["a1", "b1"], QueueTitles(room));
        Assert.True(room.IsHost("c1-again"));
        Assert.Null(room.GetConnection("c1"));

        // Their old connections are gone, so the grace running out later takes nobody away.
        _clock.Advance(WatchRoom.ReconnectGrace);
        room.Prune(_ => true);
        Assert.Equal(2, room.Snapshot().Participants.Count);
    }

    [Fact]
    public void WhenTheGraceRunsOut_ThePersonLeaves()
    {
        WatchRoom room = Join(Member("c1", "alice"));
        Join(Member("c2", "bob"));
        room.Leave("c1");
        long version = room.Snapshot().Version;

        _clock.Advance(WatchRoom.ReconnectGrace);
        room.Prune(_ => true);

        RoomStateView state = room.Snapshot();
        Assert.Equal("bob", state.HostDiscordUserId);
        Assert.Equal(["bob"], state.Participants.Select(p => p.DiscordUserId));
        Assert.Equal(version + 1, state.Version);
    }

    [Fact]
    public void ConnectionsOnAnInstanceThatStopped_GetTheirGrace()
    {
        WatchRoom room = Join(Member("c1", "alice") with { ServerId = "old" });
        Join(Member("c2", "bob") with { ServerId = "new" });
        Func<string, bool> onlyNew = server => server == "new";

        room.Prune(onlyNew);
        Assert.Equal("alice", room.Snapshot().HostDiscordUserId);

        _clock.Advance(WatchRoom.ReconnectGrace);
        room.Prune(onlyNew);
        Assert.Equal("bob", room.Snapshot().HostDiscordUserId);
        Assert.Null(room.GetConnection("c1"));
    }

    [Fact]
    public void ARoomSurvivesBeingStored()
    {
        WatchRoom room = Join(Member("c1", "alice"));
        Join(Guest("g1", "guest"));
        room.Enqueue("c1", NewClip("playing"));
        room.Enqueue("c1", NewClip("a1"));
        room.SetQueueLocked("c1", true);
        room.Leave("g1");

        WatchRoom stored = WatchRoom.FromJson(Instance, room.ToJson(), _clock);

        Assert.Equal(room.Snapshot(), stored.Snapshot(), new JsonEquality<RoomStateView>());
        Assert.Equal(room.ToJson(), stored.ToJson());
        Assert.True(stored.IsHost("c1"));
        Assert.NotNull(stored.GetConnection("g1")!.DisconnectedAt);
    }

    private WatchRoom Join(RoomConnection connection)
    {
        _room.Join(connection);
        return _room;
    }

    /// <summary>Drops the connection and lets its reconnect grace run out.</summary>
    private void LeaveForGood(string connectionId)
    {
        _room.Leave(connectionId);
        _clock.Advance(WatchRoom.ReconnectGrace);
        _room.Prune(_ => true);
    }

    private static List<string> QueueTitles(WatchRoom room) => room.Snapshot().Queue.Select(item => item.Title).ToList();

    private static Guid ItemId(WatchRoom room, string title) =>
        room.Snapshot().Queue.Single(item => item.Title == title).ItemId;

    internal static NowPlaying NewClip(string title) => new(Guid.NewGuid(), Guid.NewGuid(), title, "Apex Legends", "Alice", 30,
        new ActivityMedia("r2", "clips/video/v1", "video/mp4", HasThumbnail: true));

    private RoomConnection Member(string connectionId, string discordUserId)
    {
        _clock.Advance(TimeSpan.FromSeconds(1));
        return new RoomConnection(connectionId, discordUserId, discordUserId, null, Guid.NewGuid(), discordUserId, _clock.GetUtcNow());
    }

    private RoomConnection Guest(string connectionId, string discordUserId)
    {
        _clock.Advance(TimeSpan.FromSeconds(1));
        return new RoomConnection(connectionId, discordUserId, discordUserId, null, null, null, _clock.GetUtcNow());
    }
}

public sealed class TestClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 2, 20, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>Compares two values by their JSON, for records holding lists.</summary>
internal sealed class JsonEquality<T> : IEqualityComparer<T>
{
    public bool Equals(T? x, T? y) => System.Text.Json.JsonSerializer.Serialize(x) == System.Text.Json.JsonSerializer.Serialize(y);
    public int GetHashCode(T obj) => 0;
}
