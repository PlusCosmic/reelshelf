namespace Reelshelf.Worker;

/// <summary>A delay that doubles each time up to a ceiling, with ±20% jitter so several workers spread out.</summary>
public sealed class Backoff
{
    private readonly TimeSpan initial;
    private readonly TimeSpan max;
    private readonly Random random;
    private TimeSpan next;

    public Backoff(TimeSpan initial, TimeSpan max, Random? random = null)
    {
        this.initial = initial;
        this.max = max < initial ? initial : max;
        this.random = random ?? Random.Shared;
        next = initial;
    }

    public TimeSpan Next()
    {
        TimeSpan current = next;
        next = TimeSpan.FromTicks(Math.Min(max.Ticks, current.Ticks * 2));
        double jitter = 0.8 + random.NextDouble() * 0.4;
        return TimeSpan.FromTicks((long)(current.Ticks * jitter));
    }

    public void Reset()
    {
        next = initial;
    }
}
