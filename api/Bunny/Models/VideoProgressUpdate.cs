using System.Text.Json.Serialization;

namespace Reelshelf.Bunny.Models;

public record VideoProgressUpdate(
    [property: JsonPropertyName("VideoLibraryId")] int VideoLibraryId,
    [property: JsonPropertyName("VideoGuid")] Guid VideoGuid,
    [property: JsonPropertyName("Status")] int Status)
{
}
