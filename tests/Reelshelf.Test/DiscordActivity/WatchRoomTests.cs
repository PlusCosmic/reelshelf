using Reelshelf.DiscordActivity;
using Xunit;

namespace Reelshelf.Test.DiscordActivity;

/// <summary>
/// The in-memory watch room: who hosts, who may change playback, how playback is recorded, and when a room
/// closes. The hub is a thin layer over this.
/// </summary>
public class WatchRoomTests
{
    private const string Instance = "i-1";
    private static readonly NowPlaying Clip = new(Guid.NewGuid(), Guid.NewGuid(), "Triple", "Apex Legends", "Alice", 30);

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

        Assert.False(room.PlayClip("c2", Clip));
        Assert.False(room.PlayClip("g1", Clip));
        Assert.Null(room.Snapshot().Playback);

        Assert.True(room.PlayClip("c1", Clip));
        Assert.False(room.SetPlayback("c2", false, 5));
        Assert.False(room.Stop("g1"));
        Assert.NotNull(room.Snapshot().Playback);
    }

    [Fact]
    public void PlaybackRecordsPositionAndServerTime_ClampedToTheClip()
    {
        WatchRoom room = _rooms.Join(Instance, Member("c1", "alice"));
        Assert.False(room.SetPlayback("c1", true, 3));

        room.PlayClip("c1", Clip);
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

        room.Stop("c1");
        Assert.Null(room.Snapshot().Playback);
    }

    [Fact]
    public void EveryChangeRaisesTheVersion_RefusedOnesDoNot()
    {
        WatchRoom room = _rooms.Join(Instance, Member("c1", "alice"));
        _rooms.Join(Instance, Guest("g1", "guest"));
        long afterJoins = room.Snapshot().Version;

        room.PlayClip("g1", Clip);
        Assert.Equal(afterJoins, room.Snapshot().Version);

        room.PlayClip("c1", Clip);
        room.SetPlayback("c1", false, 1);
        Assert.Equal(afterJoins + 2, room.Snapshot().Version);
    }

    [Fact]
    public void TheLastToLeaveClosesTheRoom_AndTheNextJoinStartsAFreshOne()
    {
        WatchRoom first = _rooms.Join(Instance, Member("c1", "alice"));
        first.PlayClip("c1", Clip);

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
        Assert.False(two.PlayClip("c1", Clip));
    }

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
