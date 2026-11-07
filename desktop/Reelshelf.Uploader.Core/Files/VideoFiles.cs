namespace Reelshelf.Uploader.Core.Files;

/// <summary>A video file under the recordings folder as a scan saw it.</summary>
/// <param name="RelativePath">Below the recordings folder, with <c>/</c> between folders.</param>
public sealed record ScannedFile(string RelativePath, long Size, DateTimeOffset LastWriteUtc)
{
    /// <summary>The folder directly below the recordings folder the file is in, or "" for the folder itself.</summary>
    public string Folder => VideoFiles.FolderOf(RelativePath);

    /// <summary>The file's name without its extension, which becomes the clip's title.</summary>
    public string Title => Path.GetFileNameWithoutExtension(RelativePath.Replace('/', Path.DirectorySeparatorChar));
}

/// <summary>Which files the uploader looks at: videos in containers the site takes, not hidden ones.</summary>
public static class VideoFiles
{
    public static readonly IReadOnlySet<string> Extensions =
        new HashSet<string>([".mp4", ".mov", ".mkv", ".webm"], StringComparer.OrdinalIgnoreCase);

    public static bool HasVideoExtension(string path) => Extensions.Contains(Path.GetExtension(path));

    /// <summary>MP4 and MOV keep their length in an <c>mvhd</c> box the app can read before uploading.</summary>
    public static bool HasReadableLength(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".mp4" or ".mov";

    /// <summary>A path below the root as the ledger keys it: <c>/</c> between folders.</summary>
    public static string Normalize(string relativePath) => relativePath.Replace('\\', '/');

    public static string FolderOf(string relativePath)
    {
        string normalized = Normalize(relativePath);
        int slash = normalized.IndexOf('/');
        return slash < 0 ? "" : normalized[..slash];
    }

    /// <summary>
    /// Whether a path below the root may be a clip: a video by its extension, under no folder whose name starts with
    /// <c>.</c>, and not itself hidden by name. Attributes are checked by <see cref="IsHidden"/>.
    /// </summary>
    public static bool IsCandidate(string relativePath)
    {
        string[] segments = Normalize(relativePath).Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment.StartsWith('.')))
        {
            return false;
        }

        return HasVideoExtension(segments[^1]);
    }

    public static bool IsHidden(FileSystemInfo info) =>
        (info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;
}

/// <summary>Walks the recordings folder for candidate videos, skipping hidden and system files and dot folders.</summary>
public static class FolderScanner
{
    public static List<ScannedFile> Scan(string root)
    {
        List<ScannedFile> files = [];
        DirectoryInfo rootInfo = new(root);
        if (!rootInfo.Exists)
        {
            return files;
        }

        Walk(rootInfo, rootInfo.FullName, files);
        return files;
    }

    /// <summary>One file by its path below the root, or null when it isn't (or is no longer) a candidate.</summary>
    public static ScannedFile? Stat(string root, string relativePath)
    {
        if (!VideoFiles.IsCandidate(relativePath))
        {
            return null;
        }

        FileInfo info = new(Path.Combine(root, VideoFiles.Normalize(relativePath).Replace('/', Path.DirectorySeparatorChar)));
        try
        {
            info.Refresh();
            if (!info.Exists || VideoFiles.IsHidden(info))
            {
                return null;
            }

            return new ScannedFile(VideoFiles.Normalize(relativePath), info.Length,
                new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The folders directly below the root that hold a video somewhere inside, by name.</summary>
    public static List<string> FoldersWithVideos(string root) =>
        Scan(root).Select(file => file.Folder).Where(folder => folder.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static void Walk(DirectoryInfo directory, string root, List<ScannedFile> files)
    {
        IEnumerable<FileSystemInfo> entries;
        try
        {
            entries = directory.EnumerateFileSystemInfos().ToList();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (FileSystemInfo entry in entries)
        {
            if (entry.Name.StartsWith('.') || VideoFiles.IsHidden(entry))
            {
                continue;
            }

            if (entry is DirectoryInfo child)
            {
                // A link could loop back on itself; recorders don't save through them.
                if (child.LinkTarget is null)
                {
                    Walk(child, root, files);
                }

                continue;
            }

            if (entry is FileInfo file && VideoFiles.HasVideoExtension(file.Name))
            {
                string relative = VideoFiles.Normalize(Path.GetRelativePath(root, file.FullName));
                files.Add(new ScannedFile(relative, file.Length, new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero)));
            }
        }
    }
}
