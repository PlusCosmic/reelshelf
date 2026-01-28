namespace Nucleus.Minecraft.Models;

public record FileEntry(
    string Name,
    string Path,
    bool IsDirectory,
    long Size,
    DateTimeOffset LastModified
);
