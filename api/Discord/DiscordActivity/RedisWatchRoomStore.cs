using StackExchange.Redis;

namespace Reelshelf.Discord.DiscordActivity;

/// <summary>
/// Where watch rooms are kept. Each room is one stored JSON document (<see cref="WatchRoom"/>), saved only if it
/// is still the one the change was made from, so two changes never overwrite each other.
/// </summary>
public interface IWatchRoomStore
{
    Task<string?> LoadAsync(string instanceId);

    /// <summary>
    /// Saves <paramref name="json"/> if the room is still <paramref name="expected"/> (null: if there is no room yet),
    /// and false if another change came first.
    /// </summary>
    Task<bool> TrySaveAsync(string instanceId, string? expected, string json);

    /// <summary>Deletes the room if it is still <paramref name="expected"/>.</summary>
    Task<bool> TryDeleteAsync(string instanceId, string expected);

    /// <summary>Every open room's instance id.</summary>
    Task<IReadOnlyList<string>> ListAsync();

    Task MarkServerAliveAsync(string serverId, TimeSpan lifetime);

    Task MarkServerGoneAsync(string serverId);

    /// <summary>Which of <paramref name="serverIds"/> have a heartbeat that hasn't lapsed.</summary>
    Task<IReadOnlySet<string>> AliveServersAsync(IReadOnlyCollection<string> serverIds);
}

/// <summary>
/// Watch rooms in Redis. Under <see cref="KeyPrefix"/>, <c>room:{instanceId}</c> holds a room's JSON (expiring
/// <see cref="RoomLifetime"/> after its last change, in case it is never closed), <c>rooms</c> is the set of open
/// rooms the clock visits, and <c>server:{id}</c> is an API instance's heartbeat. Saves are transactions on a
/// <c>WATCH</c> of the room's key, so one fails when the room changed since it was read.
/// </summary>
public sealed class RedisWatchRoomStore(IConnectionMultiplexer redis, string keyPrefix) : IWatchRoomStore
{
    public static readonly TimeSpan RoomLifetime = TimeSpan.FromHours(24);

    /// <summary>
    /// Takes an id out of the set of open rooms only while its room is gone, so a room opened again in between
    /// stays listed.
    /// </summary>
    private const string ForgetIfGoneScript =
        "if redis.call('exists', KEYS[1]) == 0 then return redis.call('srem', KEYS[2], ARGV[1]) end return 0";

    /// <summary>
    /// The prefix for every key, and for the SignalR backplane's channels (<see cref="DiscordActivitySetup"/>). It
    /// carries the environment's name, so dev and prod can share one Redis without seeing each other's rooms.
    /// </summary>
    public static string KeyPrefixFor(string environmentName) => $"reelshelf:{environmentName}:watch-rooms:v1:";

    public string KeyPrefix { get; } = keyPrefix;

    private IDatabase Database => redis.GetDatabase();

    private RedisKey IndexKey => $"{KeyPrefix}rooms";

    public async Task<string?> LoadAsync(string instanceId) => await Database.StringGetAsync(RoomKey(instanceId));

    public async Task<bool> TrySaveAsync(string instanceId, string? expected, string json)
    {
        RedisKey key = RoomKey(instanceId);
        ITransaction transaction = Database.CreateTransaction();
        transaction.AddCondition(expected is null ? Condition.KeyNotExists(key) : Condition.StringEqual(key, expected));
        _ = transaction.StringSetAsync(key, json, RoomLifetime);
        _ = transaction.SetAddAsync(IndexKey, instanceId);
        return await transaction.ExecuteAsync();
    }

    public async Task<bool> TryDeleteAsync(string instanceId, string expected)
    {
        RedisKey key = RoomKey(instanceId);
        ITransaction transaction = Database.CreateTransaction();
        transaction.AddCondition(Condition.StringEqual(key, expected));
        _ = transaction.KeyDeleteAsync(key);
        _ = transaction.SetRemoveAsync(IndexKey, instanceId);
        return await transaction.ExecuteAsync();
    }

    public async Task<IReadOnlyList<string>> ListAsync()
    {
        IDatabase database = Database;
        string[] ids = (await database.SetMembersAsync(IndexKey)).Select(id => id.ToString()).ToArray();
        bool[] exists = await Task.WhenAll(ids.Select(id => database.KeyExistsAsync(RoomKey(id))));

        // A room that expired instead of closing is still listed; forget it.
        List<string> open = [];
        for (int index = 0; index < ids.Length; index++)
        {
            if (exists[index])
            {
                open.Add(ids[index]);
            }
            else
            {
                await database.ScriptEvaluateAsync(ForgetIfGoneScript, [RoomKey(ids[index]), IndexKey], [ids[index]]);
            }
        }

        return open;
    }

    public Task MarkServerAliveAsync(string serverId, TimeSpan lifetime) =>
        Database.StringSetAsync(ServerKey(serverId), "1", lifetime);

    public Task MarkServerGoneAsync(string serverId) => Database.KeyDeleteAsync(ServerKey(serverId));

    public async Task<IReadOnlySet<string>> AliveServersAsync(IReadOnlyCollection<string> serverIds)
    {
        IDatabase database = Database;
        string[] ids = serverIds.ToArray();
        bool[] alive = await Task.WhenAll(ids.Select(id => database.KeyExistsAsync(ServerKey(id))));
        return ids.Where((_, index) => alive[index]).ToHashSet(StringComparer.Ordinal);
    }

    private RedisKey RoomKey(string instanceId) => $"{KeyPrefix}room:{instanceId}";

    private RedisKey ServerKey(string serverId) => $"{KeyPrefix}server:{serverId}";
}
