using System.Security;
using Nucleus.Minecraft.Models;

namespace Nucleus.Minecraft.Services;

public class FileService(ILogger<FileService> logger)
{
    private const long MaxFileSize = 5 * 1024 * 1024; // 5MB
    private static readonly HashSet<string> EditableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cfg",
        ".conf",
        ".json",
        ".log",
        ".properties",
        ".toml",
        ".txt",
        ".yaml",
        ".yml"
    };

    private string GetSafePath(MinecraftServer server, string relativePath)
    {
        string basePath = Path.GetFullPath(server.PersistenceLocation);

        // Normalize the relative path
        string normalizedRelativePath = relativePath.Replace('\\', '/').TrimStart('/');

        // Combine with base path
        string combinedPath = Path.Combine(basePath, normalizedRelativePath);

        // Get the full normalized path
        string fullPath = Path.GetFullPath(combinedPath);

        // Verify the resulting path is still within the base path.
        string relativeToBase = Path.GetRelativePath(basePath, fullPath);
        if (relativeToBase.StartsWith("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(relativeToBase))
        {
            logger.LogWarning("Path traversal attempt detected: {RelativePath} -> {FullPath}", relativePath, fullPath);
            throw new SecurityException("Access to the specified path is denied");
        }

        return fullPath;
    }

    private static void EnsureNoSymlink(string basePath, string targetPath)
    {
        string current = basePath;
        string relative = Path.GetRelativePath(basePath, targetPath);

        foreach (string segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (string.IsNullOrWhiteSpace(segment) || segment == ".")
            {
                continue;
            }

            current = Path.Combine(current, segment);
            if ((File.Exists(current) || Directory.Exists(current)) &&
                File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
            {
                throw new SecurityException("Symbolic links are not editable through this endpoint");
            }
        }
    }

    private static void EnsureEditableFile(string safePath)
    {
        string extension = Path.GetExtension(safePath);
        if (!EditableExtensions.Contains(extension))
        {
            throw new InvalidOperationException($"Files with extension '{extension}' are not editable");
        }
    }

    public DirectoryListing ListDirectory(MinecraftServer server, string relativePath)
    {
        string safePath = GetSafePath(server, relativePath);
        string basePath = Path.GetFullPath(server.PersistenceLocation);
        EnsureNoSymlink(basePath, safePath);

        if (!Directory.Exists(safePath))
        {
            throw new DirectoryNotFoundException($"Directory not found: {relativePath}");
        }

        List<FileEntry> entries = new();

        // Add directories
        foreach (string dir in Directory.GetDirectories(safePath))
        {
            DirectoryInfo dirInfo = new(dir);
            string dirName = dirInfo.Name;
            string dirPath = Path.Combine(relativePath, dirName).Replace('\\', '/');

            entries.Add(new FileEntry(
                Name: dirName,
                Path: dirPath,
                IsDirectory: true,
                Size: 0,
                LastModified: dirInfo.LastWriteTimeUtc
            ));
        }

        // Add files
        foreach (string file in Directory.GetFiles(safePath))
        {
            FileInfo fileInfo = new(file);
            string fileName = fileInfo.Name;
            string filePath = Path.Combine(relativePath, fileName).Replace('\\', '/');

            entries.Add(new FileEntry(
                Name: fileName,
                Path: filePath,
                IsDirectory: false,
                Size: fileInfo.Length,
                LastModified: fileInfo.LastWriteTimeUtc
            ));
        }

        // Sort: directories first, then by name
        entries = entries.OrderBy(e => !e.IsDirectory).ThenBy(e => e.Name).ToList();

        return new DirectoryListing(relativePath, entries);
    }

    public async Task<string> ReadFileAsync(MinecraftServer server, string relativePath)
    {
        string safePath = GetSafePath(server, relativePath);
        string basePath = Path.GetFullPath(server.PersistenceLocation);
        EnsureNoSymlink(basePath, safePath);
        EnsureEditableFile(safePath);

        if (!File.Exists(safePath))
        {
            throw new FileNotFoundException($"File not found: {relativePath}");
        }

        FileInfo fileInfo = new(safePath);
        if (fileInfo.Length > MaxFileSize)
        {
            throw new InvalidOperationException($"File is too large to read (max {MaxFileSize / 1024 / 1024}MB)");
        }

        return await File.ReadAllTextAsync(safePath);
    }

    public async Task WriteFileAsync(MinecraftServer server, string relativePath, string content)
    {
        string safePath = GetSafePath(server, relativePath);
        string basePath = Path.GetFullPath(server.PersistenceLocation);
        EnsureNoSymlink(basePath, safePath);
        EnsureEditableFile(safePath);

        // Check content size
        long contentSize = System.Text.Encoding.UTF8.GetByteCount(content);
        if (contentSize > MaxFileSize)
        {
            throw new InvalidOperationException($"Content is too large to write (max {MaxFileSize / 1024 / 1024}MB)");
        }

        // Ensure parent directory exists
        string? parentDir = Path.GetDirectoryName(safePath);
        if (parentDir != null && !Directory.Exists(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }

        await File.WriteAllTextAsync(safePath, content);
        logger.LogInformation("File written: {Path}", relativePath);
    }

    public void DeleteFile(MinecraftServer server, string relativePath)
    {
        string safePath = GetSafePath(server, relativePath);
        string basePath = Path.GetFullPath(server.PersistenceLocation);
        EnsureNoSymlink(basePath, safePath);

        if (!File.Exists(safePath))
        {
            throw new FileNotFoundException($"File not found: {relativePath}");
        }

        File.Delete(safePath);
        logger.LogInformation("File deleted: {Path}", relativePath);
    }

    public void CreateDirectory(MinecraftServer server, string relativePath)
    {
        string safePath = GetSafePath(server, relativePath);
        string basePath = Path.GetFullPath(server.PersistenceLocation);
        EnsureNoSymlink(basePath, safePath);

        if (Directory.Exists(safePath))
        {
            throw new InvalidOperationException($"Directory already exists: {relativePath}");
        }

        Directory.CreateDirectory(safePath);
        logger.LogInformation("Directory created: {Path}", relativePath);
    }
}
