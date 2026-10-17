using Reelshelf.Video;

namespace Reelshelf.Core.Models;

/// <summary>A clip reserved for upload, and where the browser sends its bytes.</summary>
public record CreateClipResponse(Guid ClipId, Guid VideoId, UploadTarget Upload)
{
}
