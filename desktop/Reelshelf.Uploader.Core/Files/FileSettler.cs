using Reelshelf.Uploader.Core.Platform;

namespace Reelshelf.Uploader.Core.Files;

/// <summary>
/// Waits for files a recorder may still be writing. A tracked file is ready once its size and last-write time have
/// held for <see cref="StableFor"/> and it opens for reading while others may read it too (a recorder still writing
/// holds it without sharing, so the open fails with a sharing violation). <see cref="Check"/> runs every
/// <see cref="CheckEvery"/>.
/// </summary>
public sealed class FileSettler(string root, IClock clock, Func<string, bool>? canOpen = null)
{
    public static readonly TimeSpan StableFor = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(2);

    private readonly Func<string, bool> _canOpen = canOpen ?? CanOpenForReading;
    private readonly Dictionary<string, Tracked> _tracked = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _tracked.Count;
            }
        }
    }

    public bool IsTracking(string relativePath)
    {
        lock (_lock)
        {
            return _tracked.ContainsKey(VideoFiles.Normalize(relativePath));
        }
    }

    /// <summary>Starts watching a file, or carries on watching it.</summary>
    public void Track(string relativePath)
    {
        lock (_lock)
        {
            _tracked.TryAdd(VideoFiles.Normalize(relativePath), new Tracked());
        }
    }

    /// <summary>
    /// Looks at every tracked file once: answers the ones that have settled, and stops tracking them and any that are
    /// gone.
    /// </summary>
    public List<ScannedFile> Check()
    {
        List<ScannedFile> ready = [];
        DateTimeOffset now = clock.UtcNow;
        lock (_lock)
        {
            foreach ((string path, Tracked tracked) in _tracked.ToList())
            {
                ScannedFile? current = FolderScanner.Stat(root, path);
                if (current is null)
                {
                    _tracked.Remove(path);
                    continue;
                }

                if (tracked.Last is null || tracked.Last.Size != current.Size ||
                    tracked.Last.LastWriteUtc != current.LastWriteUtc)
                {
                    tracked.Last = current;
                    tracked.StableSince = now;
                    continue;
                }

                if (now - tracked.StableSince < StableFor)
                {
                    continue;
                }

                if (!_canOpen(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar))))
                {
                    continue;
                }

                _tracked.Remove(path);
                ready.Add(current);
            }
        }

        return ready;
    }

    /// <summary>Opens the file for reading, sharing it only with readers; a writer still holding it makes this fail.</summary>
    public static bool CanOpenForReading(string path)
    {
        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed class Tracked
    {
        public ScannedFile? Last { get; set; }

        public DateTimeOffset StableSince { get; set; }
    }
}
