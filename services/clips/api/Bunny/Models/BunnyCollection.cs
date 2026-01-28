namespace Nucleus.Clips.Bunny.Models;

public record BunnyCollection(long VideoLibraryId, Guid Guid, string Name, long VideoCount, long TotalSize, string PreviewVideoIds, string[] PreviewImageUrls)
{
}
