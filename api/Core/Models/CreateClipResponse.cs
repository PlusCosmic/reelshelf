namespace Reelshelf.Core.Models;

public record CreateClipResponse(Guid ClipId, string Signature, long Expiration, string LibraryId, Guid VideoId, Guid CollectionId)
{
}
