namespace Reelshelf.Uploader.Core.Files;

/// <summary>
/// The recordings folder's <see cref="FileSystemWatcher"/>. Its events only say a path is worth a look
/// (<see cref="Dirty"/>); the watcher can drop events when its buffer overflows, so <see cref="RescanNeeded"/> asks for
/// a full rescan then, as it does when a folder appears (its files raise no events of their own). The periodic rescan
/// catches anything else missed.
/// </summary>
public sealed class FolderWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly string _root;

    public FolderWatcher(string root)
    {
        _root = Path.GetFullPath(root);
        _watcher = new FileSystemWatcher(_root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size |
                           NotifyFilters.LastWrite,
            // The largest buffer Windows allows, so bursts from a recorder rarely overflow it.
            InternalBufferSize = 64 * 1024
        };
        _watcher.Created += (_, e) => OnPath(e.FullPath);
        _watcher.Changed += (_, e) => OnPath(e.FullPath);
        _watcher.Renamed += (_, e) => OnPath(e.FullPath);
        _watcher.Error += (_, _) => RescanNeeded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A path below the root, with <c>/</c> between folders, that may have changed.</summary>
    public event EventHandler<string>? Dirty;

    public event EventHandler? RescanNeeded;

    public void Start() => _watcher.EnableRaisingEvents = true;

    private void OnPath(string fullPath)
    {
        string relative = VideoFiles.Normalize(Path.GetRelativePath(_root, fullPath));
        if (Directory.Exists(fullPath))
        {
            RescanNeeded?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (VideoFiles.IsCandidate(relative))
        {
            Dirty?.Invoke(this, relative);
        }
    }

    public void Dispose()
    {
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
    }
}
