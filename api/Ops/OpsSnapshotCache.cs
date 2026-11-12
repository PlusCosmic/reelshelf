using System.Collections.Concurrent;

namespace Reelshelf.Ops;

/// <summary>
/// Keeps each ops response for <see cref="Lifetime"/> in memory, so the monitor app (and several of them) can poll
/// without each poll running the aggregates again. Requests that miss together wait for one load.
/// </summary>
public sealed class OpsSnapshotCache(TimeProvider timeProvider)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(15);

    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    public async Task<T> GetAsync<T>(string key, Func<CancellationToken, Task<T>> load,
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
            entry.Current = new Snapshot(value, timeProvider.GetUtcNow());
            return value;
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    private T? Fresh<T>(Entry entry) where T : class =>
        entry.Current is { } snapshot && timeProvider.GetUtcNow() - snapshot.At < Lifetime ? snapshot.Value as T : null;

    private sealed record Snapshot(object Value, DateTimeOffset At);

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
