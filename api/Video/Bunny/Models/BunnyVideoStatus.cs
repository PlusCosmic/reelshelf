namespace Reelshelf.Video.Bunny.Models;

/// <summary>
/// Status codes in the body of Bunny's video progress webhook. Not the status on Bunny's video object, which is
/// what clips store: that is <see cref="BunnyVideoObjectStatus"/>.
/// See: https://docs.bunny.net/docs/stream-webhook
/// </summary>
public enum BunnyVideoStatus
{
    Queued = 0,
    Processing = 1,
    Encoding = 2,
    Finished = 3,
    ResolutionFinished = 4,
    Failed = 5,
    PresignedUploadStarted = 6,
    PresignedUploadFinished = 7,
    PresignedUploadFailed = 8,
    CaptionsGenerated = 9
}
