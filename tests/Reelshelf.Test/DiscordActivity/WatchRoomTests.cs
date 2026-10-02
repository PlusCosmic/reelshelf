using Reelshelf.DiscordActivity;
using Xunit;

namespace Reelshelf.Test.DiscordActivity;

/// <summary>
/// The in-memory watch room: who hosts, who may change playback and the queue, how the queue is ordered, how
/// playback is recorded, who may react and how often, and when a room closes. The hub is a thin layer over this.
/// </summary>
public class WatchRoomTests
{
    private const string Instance = "i-1";
    private static readonly NowPlaying Clip = NewClip("Triple");

    private readonly Clock _clock = new();
    private readonly WatchRoomRegistry _rooms;

    public WatchRoomTests()
    {
        _rooms = new WatchRoomRegistry(_clock);
    }

    [Fact]
    public void TheFirstMemberHosts_GuestsNever()
    {
        _rooms.Join(Instance, Guest("g1", "guest"));
        Assert.Null(_rooms.Find(Instance)!.Snapshot().HostDiscordUserId);

        WatchRoom room = _rooms.Join(Instance, Member("c1", "alice"));
        _rooms.Join(Instance, Member("c2", "bob"));

        RoomStateView state = room.Snapshot();
        Assert.Equal("alice", state.HostDiscordUserId);
        Assert.Equal(["guest", "alice", "bob"], state.Participants.Select(p => p.DiscordUserId));
        Assert.True(state.Participants.Single(p => p.DiscordUserId == "alice").IsHost);
        Assert.False(state.Participants.Single(p => p.DiscordUserId == "guest").IsMember);
    }

    [Fact]
    public void WhenTheHostLeaves_TheLongestConnectedMemberTakesOver()
    {
        WatchRoom room = _rooms.Join(Instance, Member("c1", "alice"));
        _rooms.Join(Instance, Guest("g1", "guest"));
        _rooms.Join(Instance, Member("c2", "bob"));
        _rooms.Join(Instance, Member("c3", "carol"));

        _rooms.Leave(Instance, "c1");
        Assert.Equal("bob", room.Snapshot().HostDiscordUserId);

        _rooms.Leave(Instance, "c2");
        _rooms.Leave(Instance, "c3");
        Assert.Null(room.Snapshot().HostDiscordUserId);
    }

    [Fact]
    public void TheHostKeepsTheRole_WhileAnyOfTheirConnectionsRemain()
    {
        WatchRoom room = _rooms.Join(Instance, Member("desktop", "alice"));
        _rooms.Join(Instance, Member("c2", "bob"));
        _rooms.Join(Instance, Member("phone", "alice"));

        _rooms.Leave(Instance, "desktop");

        RoomStateView state = room.Snapshot();
        Assert.Equal("alice", state.HostDiscordUserId);
        Assert.Equal(2, state.Participants.Count);
        Assert.True(room.IsHost("phone"));
    }

    [Fact]
    public void OnlyTheHostChangesPlayback()
    {
        WatchRoom room = _rooms.Join(Instance, Member("c1", "alice"));
        _rooms.Join(Instance, Member("c2", "bob"));
        _rooms.Join(Instance, Guest("g1", "guest"));

        Assert.Equal(EnqueueResult.NotAMember, room.Enqueue("g1", Clip));
        Assert.Equal(EnqueueResult.Queued, room.Enqueue("c2", Clip));
        Assert.Equal(Clip, room.Snapshot().Playback!.Clip);

        Assert.False(room.SetPlayback("c2", false, 5));
        Assert.False(room.SetPlayback("g1", false, 5));
        Assert.False(room.Next("c2", null));
        Assert.False(room.SetQueueLocked("c2", true));
        Assert.True(room.SetPlayback("c1", false, 5));
        Assert.False(room.Snapshot().Playback!.Playing);
    }

    [Fact]
    public void PlaybackRecordsPositionAndServerTime_ClampedToTheClip()
    {
        WatchRoom room = _rooms.Join(Instance, Member("c1", "alice"));
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

        room.Next("c1", null);
        Assert.Null(room.Snapshot().Playback);
    }

    [Fact]
    public void EveryChangeRaisesTheVersion_RefusedOnesDoNot()
    {
        WatchRoom room = _rooms.Join(Instance, Member("c1", "alice"));
        _rooms.Join(Instance, Guest("g1", "guest"));
        long afterJoins = room.Snapshot().Version;

        room.Enqueue("g1", Clip);
        Assert.Equal(afterJoins, room.Snapshot().Version);

        room.Enqueue("c1", Clip);
        room.SetPlayback("c1", false, 1);
        Assert.Equal(afterJoins + 2, room.Snapshot().Version);
    }

    [Fact]
    public void TheLastToLeaveClosesTheRoom_AndTheNextJoinStartsAFreshOne()
    {
        WatchRoom first = _rooms.Join(Instance, Member("c1", "alice"));
        first.Enqueue("c1", Clip);

        Assert.Null(_rooms.Leave(Instance, "c1"));
        Assert.Null(_rooms.Find(Instance));

        WatchRoom second = _rooms.Join(Instance, Member("c2", "bob"));
        Assert.NotSame(first, second);
        Assert.Null(second.Snapshot().Playback);
        Assert.Equal("bob", second.Snapshot().HostDiscordUserId);
    }

    [Fact]
    public void RoomsAreSeparatePerInstance()
    {
        WatchRoom one = _rooms.Join("i-1", Member("c1", "alice"));
        WatchRoom two = _rooms.Join("i-2", Member("c2", "bob"));

        Assert.NotSame(one, two);
        Assert.Equal(EnqueueResult.NotAMember, two.Enqueue("c1", Clip));
    }

    [Fact]
    public void TheQueueTakesTurnsByWhoQueued()
    {
        WatchRoom room = _rooms.Join(Instance, Member("c1", "alice"));
        _rooms.Join(Instance, Member("c2", "bob"));
        _rooms.Join(Instance, Member("c3", "carol"));

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
        WatchRoom room = _rooms.Join(Instance, Member("c1", "alice"));
        _rooms.Join(Instance, Member("c2", "bob"));
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
        WatchRoom room = _rooms.Join(Instance, Member("c1", "alice"));
        _rooms.Join(Instance, Member("c2", "bob"));
        _rooms.Join(Instance, Member("c3", "carol"));
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
        WatchRoom room = _rooms.Join(Instance, Member("c1", "alice"));
        _rooms.Join(Instance, Member("c2", "bob"));
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
        WatchRoom room = _rooms.Join(Instance, Member("c1", "alice"));
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
        WatchRoom room = _rooms.Join(Instance, Member("c1", "alice"));
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
    public void AnAbsentOwnersClipsAreHiddenAndSkipped_UntilTheyReturn()
    {
        WatchRoom room = _rooms.Join(Instance, Member("c1", "alice"));
        _rooms.Join(Instance, Member("c2", "bob"));
        room.Enqueue("c1", NewClip("playing"));
        room.Enqueue("c2", NewClip("b1"));
        room.Enqueue("c1", NewClip("a1"));
        Assert.Equal(["b1", "a1"], QueueTitles(room));

        _rooms.Leave(Instance, "c2");
        Assert.Equal(["a1"], QueueTitles(room));
        Assert.Equal("playing", room.Snapshot().Playback!.Clip.Title);

        // Bob comes back before his turn: his clip is still there.
        _rooms.Join(Instance, Member("c2b", "bob"));
        Assert.Equal(["b1", "a1"], QueueTitles(room));

        // He leaves again; when his turn comes his clip is dropped.
        _rooms.Leave(Instance, "c2b");
        room.Next("c1", null);
        Assert.Equal("a1", room.Snapshot().Playback!.Clip.Title);
        _rooms.Join(Instance, Member("c2c", "bob"));
        Assert.Empty(room.Snapshot().Queue);
    }

    [Fact]
    public void WhenOnlyGuestsAreLeft_NothingPlaysAndTheQueueEmpties()
    {
        WatchRoom room = _rooms.Join(Instance, Member("c1", "alice"));
        _rooms.Join(Instance, Guest("g1", "guest"));
        room.Enqueue("c1", NewClip("playing"));
        room.Enqueue("c1", NewClip("next"));
        room.SetQueueLocked("c1", true);

        _rooms.Leave(Instance, "c1");
        RoomStateView state = room.Snapshot();
        Assert.Null(state.Playback);
        Assert.Empty(state.Queue);
        Assert.False(state.QueueLocked);

        // Alice coming back finds an empty room, not her old queue.
        _rooms.Join(Instance, Member("c1b", "alice"));
        Assert.Empty(room.Snapshot().Queue);
    }

    [Fact]
    public void AnyoneReactsToThePlayingClip_WithAnEmojiOnOffer()
    {
        WatchRoom room = _rooms.Join(Instance, Member("c1", "alice"));
        _rooms.Join(Instance, Guest("g1", "guest"));
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
    public void ReactionsAreLimitedPerPerson_AndTheLimitRecovers()
    {
        WatchRoom room = _rooms.Join(Instance, Member("c1", "alice"));
        _rooms.Join(Instance, Member("c1-phone", "alice"));
        _rooms.Join(Instance, Guest("g1", "guest"));
        room.Enqueue("c1", Clip);

        for (int i = 0; i < WatchRoom.ReactionsPerWindow; i++)
        {
            Assert.NotNull(room.React(i % 2 == 0 ? "c1" : "c1-phone", "😂"));
        }

        Assert.Null(room.React("c1", "😂"));
        Assert.Null(room.React("c1-phone", "😂"));
        Assert.NotNull(room.React("g1", "😂"));

        _clock.Advance(WatchRoom.ReactionWindow);
        Assert.NotNull(room.React("c1", "😂"));
    }

    private static List<string> QueueTitles(WatchRoom room) => room.Snapshot().Queue.Select(item => item.Title).ToList();

    private static Guid ItemId(WatchRoom room, string title) =>
        room.Snapshot().Queue.Single(item => item.Title == title).ItemId;

    private static NowPlaying NewClip(string title) => new(Guid.NewGuid(), Guid.NewGuid(), title, "Apex Legends", "Alice", 30);

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

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 2, 20, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
