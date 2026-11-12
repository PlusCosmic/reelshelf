using System.Collections.Concurrent;

namespace Reelshelf.Ops;

/// <summary>
/// Keeps each ops response for <see cref="Lifetime"/> (or as long as its caller says) in memory, so the monitor app (and several of them) can poll
/// without each poll running the aggregates again. Requests that miss together wait for one load.
/// </summary>
public sealed class OpsSnapshotCache(TimeProvider timeProvider)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(15);

    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    public Task<T> GetAsync<T>(string key, Func<CancellationToken, Task<T>> load,
        CancellationToken cancellationToken) where T : class =>
        GetAsync(key, _ => Lifetime, load, cancellationToken);

    /// <summary>As above, each value kept for as long as <paramref name="lifetime"/> says of it.</summary>
    public async Task<T> GetAsync<T>(string key, Func<T, TimeSpan> lifetime, Func<CancellationToken, Task<T>> load,
        CancellationToken cancellationToken) where T : class
    {
        Entry entry = _entries.GetOrAdd(key, _ => new Entry());
        if (Fresh<T>(entry) is { } hit)
        {
            return hit;
        }

        await entry.Gate.WaitAsync(cancellationToken);
        try
        {
            if (Fresh<T>(entry) is { } loaded)
            {
                return loaded;
            }

            T value = await load(cancellationToken);
            entry.Current = new Snapshot(value, timeProvider.GetUtcNow() + lifetime(value));
            return value;
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    private T? Fresh<T>(Entry entry) where T : class =>
        entry.Current is { } snapshot && timeProvider.GetUtcNow() < snapshot.Until ? snapshot.Value as T : null;

    private sealed record Snapshot(object Value, DateTimeOffset Until);

    private sealed class Entry
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);

        private volatile Snapshot? _current;

        public Snapshot? Current
        {
            get => _current;
            set => _current = value;
        }
    }
}
