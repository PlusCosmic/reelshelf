namespace Reelshelf.Video.Transcoding.Overflow;

/// <summary>Starts and counts the overflow pool's tasks, one worker each; a fake stands in for Batch in tests.</summary>
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
