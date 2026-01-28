namespace Nucleus.Minecraft.Models;

public record DirectoryListing(
    string CurrentPath,
    List<FileEntry> Entries
);
