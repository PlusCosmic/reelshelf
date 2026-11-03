namespace Reelshelf.Video.Transcoding.Overflow;

/// <summary>
/// Starts and counts one overflow pool's tasks, one worker each; a fake stands in for the backend in tests.
/// </summary>
public interface IOverflowTaskLauncher
{
    /// <summary>The overflow tasks running or still provisioning.</summary>
    Task<int> CountActiveTasksAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Starts up to <paramref name="count"/> tasks and returns how many the backend accepted. Throws when nothing could
    /// be asked of it at all.
    /// </summary>
    Task<int> StartTasksAsync(int count, CancellationToken cancellationToken);
}

/// <summary>One backend of the overflow pool: its name in <see cref="OverflowOptions.Pools"/> and its launcher.</summary>
public sealed record OverflowPool(string Name, IOverflowTaskLauncher Launcher);

/// <summary>The pools in <see cref="OverflowOptions.Pools"/>'s order, most preferred first.</summary>
public sealed class OverflowPools(IReadOnlyList<OverflowPool> pools)
{
    public IReadOnlyList<OverflowPool> All { get; } = pools;
}
