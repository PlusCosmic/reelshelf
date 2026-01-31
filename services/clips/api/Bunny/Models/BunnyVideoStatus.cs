namespace Nucleus.Clips.Bunny.Models;

/// <summary>
/// Video encoding status codes from Bunny CDN Stream API.
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
