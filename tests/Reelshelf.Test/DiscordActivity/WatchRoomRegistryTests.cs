using Reelshelf.Discord.DiscordActivity;
using StackExchange.Redis;
using Xunit;

namespace Reelshelf.Test.DiscordActivity;

/// <summary>
/// Rooms as the hub sees them: opened and closed, kept per Activity instance, changed by any API instance without
/// losing a change, and kept through a deploy. Runs against an in-memory store here and against Redis in
/// <see cref="RedisWatchRoomRegistryTests"/>.
/// </summary>
public abstract class WatchRoomRegistryTestsBase
{
    protected const string Instance = "i-1";

    protected readonly TestClock Clock = new();

    protected abstract IWatchRoomStore Store { get; }

    /// <summary>A registry on an API instance of its own, with its heartbeat written as its clock would.</summary>
    protected WatchRoomRegistry NewRegistry(WatchRoomServer? server = null)
    {
        server ??= new WatchRoomServer();
        Store.MarkServerAliveAsync(server.Id, WatchRoomServer.HeartbeatLifetime).GetAwaiter().GetResult();
        return new WatchRoomRegistry(Store, server, new ReactionLimiter(Clock), Clock);
    }

    protected async Task TheLastToLeaveClosesTheRoom_OnceTheirGraceIsUp_AndTheNextJoinStartsAFreshOne()
    {
        WatchRoomRegistry rooms = NewRegistry();
        await rooms.JoinAsync(Instance, Member("c1", "alice"));
        await rooms.ApplyAsync(Instance, r => r.Enqueue("c1", WatchRoomTests.NewClip("first")) == EnqueueResult.Queued);

        WatchRoomUpdate? left = await rooms.LeaveAsync(Instance, "c1");
        Assert.False(left!.Changed);
        Assert.NotNull(await rooms.FindAsync(Instance));
        Assert.Equal([Instance], await Store.ListAsync());

        Clock.Advance(WatchRoom.ReconnectGrace);
        Assert.Empty(await rooms.TickAsync());
        Assert.Null(await rooms.FindAsync(Instance));
        Assert.Empty(await Store.ListAsync());

        RoomStateView fresh = await rooms.JoinAsync(Instance, Member("c2", "bob"));
        Assert.Null(fresh.Playback);
        Assert.Equal("bob", fresh.HostDiscordUserId);
    }

    protected async Task RoomsAreSeparatePerInstance()
    {
        WatchRoomRegistry rooms = NewRegistry();
        await rooms.JoinAsync("i-1", Member("c1", "alice"));
        await rooms.JoinAsync("i-2", Member("c2", "bob"));

        WatchRoomUpdate? update = await rooms.ApplyAsync("i-2",
            r => r.Enqueue("c1", WatchRoomTests.NewClip("x")) == EnqueueResult.Queued);
        Assert.False(update!.Applied);
        Assert.Null(await rooms.ApplyAsync("i-3", _ => true));
        Assert.Equal(["i-1", "i-2"], (await Store.ListAsync()).Order());
    }

    protected async Task AChangeAndItsStateAreTakenTogether()
    {
        WatchRoomRegistry rooms = NewRegistry();
        await rooms.JoinAsync(Instance, Member("c1", "alice"));
        await rooms.JoinAsync(Instance, Member("c2", "bob"));
        NowPlaying clip = WatchRoomTests.NewClip("clip");

        WatchRoomUpdate applied = (await rooms.ApplyAsync(Instance, r => r.Enqueue("c1", clip) == EnqueueResult.Queued))!;
        Assert.True(applied.Applied);
        Assert.True(applied.Changed);
        Assert.Equal(clip, applied.State!.Playback!.Clip);

        WatchRoomUpdate refused = (await rooms.ApplyAsync(Instance, r => r.SetQueueLocked("c2", true)))!;
        Assert.False(refused.Applied);
        Assert.False(refused.Changed);
        Assert.Equal(applied.State.Version, refused.State!.Version);
    }

    protected async Task TheClockMovesOnAnOverdueClip_Once_WhicheverInstanceSeesItFirst()
    {
        WatchRoomRegistry first = NewRegistry();
        WatchRoomRegistry second = NewRegistry();
        await first.JoinAsync(Instance, Member("c1", "alice"));
        await first.ApplyAsync(Instance, r => r.Enqueue("c1", WatchRoomTests.NewClip("one")) == EnqueueResult.Queued);
        await first.ApplyAsync(Instance, r => r.Enqueue("c1", WatchRoomTests.NewClip("two")) == EnqueueResult.Queued);
        Assert.Empty(await first.TickAsync());

        Clock.Advance(TimeSpan.FromSeconds(30) + WatchRoom.EndGrace);
        (string instanceId, RoomStateView state) = Assert.Single(await second.TickAsync());
        Assert.Equal(Instance, instanceId);
        Assert.Equal("two", state.Playback!.Clip.Title);
        Assert.Empty(await first.TickAsync());
    }

    protected async Task ChangesFromTwoInstancesAtOnce_AreAllKept()
    {
        WatchRoomRegistry first = NewRegistry();
        WatchRoomRegistry second = NewRegistry();
        await first.JoinAsync(Instance, Member("c1", "alice"));
        await second.JoinAsync(Instance, Member("c2", "bob"));

        await Task.WhenAll(Enumerable.Range(0, 20).Select(index => Task.Run(() =>
            (index % 2 == 0 ? first : second).ApplyAsync(Instance, r =>
                r.Enqueue(index % 2 == 0 ? "c1" : "c2", WatchRoomTests.NewClip($"clip {index}")) == EnqueueResult.Queued))));

        RoomStateView state = (await first.FindAsync(Instance))!.Snapshot();
        Assert.Equal(19, state.Queue.Count);
        Assert.NotNull(state.Playback);
    }

    protected async Task ADeployKeepsTheRoom()
    {
        // The old container has the room; the new one starts, everyone drops off the old one and reconnects.
        WatchRoomServer oldServer = new();
        WatchRoomRegistry before = NewRegistry(oldServer);
        await before.JoinAsync(Instance, Member("c1", "alice"));
        await before.JoinAsync(Instance, Member("c2", "bob"));
        await before.ApplyAsync(Instance, r => r.Enqueue("c1", WatchRoomTests.NewClip("playing")) == EnqueueResult.Queued);
        await before.ApplyAsync(Instance, r => r.Enqueue("c2", WatchRoomTests.NewClip("b1")) == EnqueueResult.Queued);
        await before.LeaveAsync(Instance, "c1");
        await Store.MarkServerGoneAsync(oldServer.Id); // Bob's connection closed without a word.

        WatchRoomRegistry after = NewRegistry();
        Clock.Advance(TimeSpan.FromSeconds(3));
        await after.JoinAsync(Instance, Member("c2-new", "bob"));
        RoomStateView state = await after.JoinAsync(Instance, Member("c1-new", "alice"));

        Assert.Equal("alice", state.HostDiscordUserId);
        Assert.Equal("playing", state.Playback!.Clip.Title);
        Assert.Equal(["b1"], state.Queue.Select(item => item.Title));
        Assert.Equal(["alice", "bob"], state.Participants.Select(p => p.DiscordUserId));

        Clock.Advance(WatchRoom.ReconnectGrace);
        await after.TickAsync();
        WatchRoom room = (await after.FindAsync(Instance))!;
        Assert.True(room.IsHost("c1-new"));
        Assert.Null(room.GetConnection("c2"));
    }

    protected async Task ReactionsAreLimitedPerPerson_AndTheLimitRecovers()
    {
        WatchRoomRegistry rooms = NewRegistry();
        await rooms.JoinAsync(Instance, Member("c1", "alice"));
        await rooms.JoinAsync(Instance, Member("c1-phone", "alice"));
        await rooms.JoinAsync(Instance, Guest("g1", "guest"));
        Assert.Null(await rooms.ReactAsync(Instance, "g1", "😂"));
        await rooms.ApplyAsync(Instance, r => r.Enqueue("c1", WatchRoomTests.NewClip("clip")) == EnqueueResult.Queued);

        for (int i = 0; i < WatchRoom.ReactionsPerWindow; i++)
        {
            Assert.NotNull(await rooms.ReactAsync(Instance, i % 2 == 0 ? "c1" : "c1-phone", "😂"));
        }

        Assert.Null(await rooms.ReactAsync(Instance, "c1", "😂"));
        Assert.Null(await rooms.ReactAsync(Instance, "c1-phone", "😂"));
        Assert.NotNull(await rooms.ReactAsync(Instance, "g1", "😂"));

        Clock.Advance(WatchRoom.ReactionWindow);
        Assert.NotNull(await rooms.ReactAsync(Instance, "c1", "😂"));
    }

    protected RoomConnection Member(string connectionId, string discordUserId)
    {
        Clock.Advance(TimeSpan.FromSeconds(1));
        return new RoomConnection(connectionId, discordUserId, discordUserId, null, Guid.NewGuid(), discordUserId,
            Clock.GetUtcNow());
    }

    protected RoomConnection Guest(string connectionId, string discordUserId)
    {
        Clock.Advance(TimeSpan.FromSeconds(1));
        return new RoomConnection(connectionId, discordUserId, discordUserId, null, null, null, Clock.GetUtcNow());
    }
}

public sealed class WatchRoomRegistryTests : WatchRoomRegistryTestsBase
{
    protected override IWatchRoomStore Store { get; } = new InMemoryWatchRoomStore();

    [Fact]
    public Task TheLastToLeaveClosesTheRoom() => TheLastToLeaveClosesTheRoom_OnceTheirGraceIsUp_AndTheNextJoinStartsAFreshOne();

    [Fact]
    public Task RoomsAreSeparate() => RoomsAreSeparatePerInstance();

    [Fact]
    public Task ChangeAndState() => AChangeAndItsStateAreTakenTogether();

    [Fact]
    public Task OverdueClip() => TheClockMovesOnAnOverdueClip_Once_WhicheverInstanceSeesItFirst();

    [Fact]
    public Task ConcurrentChanges() => ChangesFromTwoInstancesAtOnce_AreAllKept();

    [Fact]
    public Task Deploy() => ADeployKeepsTheRoom();

    [Fact]
    public Task Reactions() => ReactionsAreLimitedPerPerson_AndTheLimitRecovers();

    [Fact]
    public async Task AChangeRetriesFromTheNewerRoom_WhenAnotherSavedFirst()
    {
        WatchRoomRegistry rooms = NewRegistry();
        await rooms.JoinAsync(Instance, Member("c1", "alice"));
        await rooms.JoinAsync(Instance, Member("c2", "bob"));
        int runs = 0;

        WatchRoomUpdate update = (await rooms.ApplyAsync(Instance, room =>
        {
            if (runs++ == 0)
            {
                // Another instance queues a clip between this change's read and its save.
                NewRegistry().ApplyAsync(Instance,
                    r => r.Enqueue("c2", WatchRoomTests.NewClip("bob's")) == EnqueueResult.Queued).GetAwaiter().GetResult();
            }

            return room.Enqueue("c1", WatchRoomTests.NewClip("alice's")) == EnqueueResult.Queued;
        }))!;

        Assert.Equal(2, runs);
        Assert.Equal("bob's", update.State!.Playback!.Clip.Title);
        Assert.Equal(["alice's"], update.State.Queue.Select(item => item.Title));
    }
}

/// <summary>The same against a real Redis, with <c>REELSHELF_TEST_REDIS</c> set (CI runs one).</summary>
public sealed class RedisWatchRoomRegistryTests : WatchRoomRegistryTestsBase, IAsyncLifetime
{
    private ConnectionMultiplexer? _redis;
    private RedisWatchRoomStore? _store;

    protected override IWatchRoomStore Store => _store!;

    public async Task InitializeAsync()
    {
        if (RedisFactAttribute.ConnectionString is not { } connectionString)
        {
            return;
        }

        _redis = await ConnectionMultiplexer.ConnectAsync(connectionString);
        _store = new RedisWatchRoomStore(_redis, $"reelshelf:test:{Guid.NewGuid():N}:");
    }

    public async Task DisposeAsync()
    {
        if (_redis is null)
        {
            return;
        }

        IServer server = _redis.GetServers()[0];
        await foreach (RedisKey key in server.KeysAsync(pattern: $"{_store!.KeyPrefix}*"))
        {
            await _redis.GetDatabase().KeyDeleteAsync(key);
        }

        await _redis.DisposeAsync();
    }

    [RedisFact]
    public Task TheLastToLeaveClosesTheRoom() => TheLastToLeaveClosesTheRoom_OnceTheirGraceIsUp_AndTheNextJoinStartsAFreshOne();

    [RedisFact]
    public Task RoomsAreSeparate() => RoomsAreSeparatePerInstance();

    [RedisFact]
    public Task ChangeAndState() => AChangeAndItsStateAreTakenTogether();

    [RedisFact]
    public Task OverdueClip() => TheClockMovesOnAnOverdueClip_Once_WhicheverInstanceSeesItFirst();

    [RedisFact]
    public Task ConcurrentChanges() => ChangesFromTwoInstancesAtOnce_AreAllKept();

    [RedisFact]
    public Task Deploy() => ADeployKeepsTheRoom();

    [RedisFact]
    public Task Reactions() => ReactionsAreLimitedPerPerson_AndTheLimitRecovers();

    [RedisFact]
    public async Task ASaveFails_WhenTheRoomChangedSinceItWasRead()
    {
        Assert.True(await Store.TrySaveAsync(Instance, null, "one"));
        Assert.False(await Store.TrySaveAsync(Instance, null, "two"));
        Assert.False(await Store.TrySaveAsync(Instance, "stale", "two"));
        Assert.True(await Store.TrySaveAsync(Instance, "one", "two"));
        Assert.False(await Store.TryDeleteAsync(Instance, "one"));
        Assert.Equal("two", await Store.LoadAsync(Instance));
        Assert.True(await Store.TryDeleteAsync(Instance, "two"));
        Assert.Null(await Store.LoadAsync(Instance));
    }

    [RedisFact]
    public async Task AnExpiredRoomIsForgotten_AndHeartbeatsLapse()
    {
        await Store.TrySaveAsync(Instance, null, "{}");
        await _redis!.GetDatabase().KeyDeleteAsync($"{_store!.KeyPrefix}room:{Instance}");
        Assert.Empty(await Store.ListAsync());
        Assert.Empty(await _redis.GetDatabase().SetMembersAsync($"{_store.KeyPrefix}rooms"));

        await Store.MarkServerAliveAsync("a", TimeSpan.FromMilliseconds(200));
        await Store.MarkServerAliveAsync("b", TimeSpan.FromMinutes(1));
        Assert.Equal(["a", "b"], (await Store.AliveServersAsync(["a", "b", "c"])).Order());
        await Task.Delay(400);
        Assert.Equal(["b"], await Store.AliveServersAsync(["a", "b", "c"]));
        await Store.MarkServerGoneAsync("b");
        Assert.Empty(await Store.AliveServersAsync(["b"]));
    }
}

/// <summary>A test that needs Redis. It runs when <c>REELSHELF_TEST_REDIS</c> holds a connection string, as in CI.</summary>
public sealed class RedisFactAttribute : FactAttribute
{
    public const string ConnectionStringVariable = "REELSHELF_TEST_REDIS";

    public RedisFactAttribute()
    {
        if (ConnectionString is null)
        {
            Skip = $"Set {ConnectionStringVariable} to a Redis connection string to run this test";
        }
    }

    public static string? ConnectionString =>
        Environment.GetEnvironmentVariable(ConnectionStringVariable) is { Length: > 0 } value ? value : null;
}

/// <summary>Rooms kept in a dictionary, with the same compare-and-save as Redis.</summary>
internal sealed class InMemoryWatchRoomStore : IWatchRoomStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _rooms = new(StringComparer.Ordinal);
    private readonly HashSet<string> _servers = new(StringComparer.Ordinal);

    public Task<string?> LoadAsync(string instanceId)
    {
        lock (_gate)
        {
            return Task.FromResult(_rooms.GetValueOrDefault(instanceId));
        }
    }

    public Task<bool> TrySaveAsync(string instanceId, string? expected, string json)
    {
        lock (_gate)
        {
            if (_rooms.GetValueOrDefault(instanceId) != expected)
            {
                return Task.FromResult(false);
            }

            _rooms[instanceId] = json;
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryDeleteAsync(string instanceId, string expected)
    {
        lock (_gate)
        {
            return Task.FromResult(_rooms.GetValueOrDefault(instanceId) == expected && _rooms.Remove(instanceId));
        }
    }

    public Task<IReadOnlyList<string>> ListAsync()
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<string>>(_rooms.Keys.ToList());
        }
    }

    public Task MarkServerAliveAsync(string serverId, TimeSpan lifetime)
    {
        lock (_gate)
        {
            _servers.Add(serverId);
        }

        return Task.CompletedTask;
    }

    public Task MarkServerGoneAsync(string serverId)
    {
        lock (_gate)
        {
            _servers.Remove(serverId);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlySet<string>> AliveServersAsync(IReadOnlyCollection<string> serverIds)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlySet<string>>(serverIds.Where(_servers.Contains).ToHashSet());
        }
    }
}
