namespace Reelshelf.Bunny.Models;

/// <summary>
/// The <c>status</c> on Bunny's video object, which is what <see cref="BunnyVideo.Status"/> carries and what is
/// stored in <c>clip.video_status</c>. Bunny's webhooks report progress with a different list
/// (<see cref="BunnyVideoStatus"/>) that numbers the same steps differently, so never compare one with the other.
/// See: https://docs.bunny.net/reference/video_getvideo
/// </summary>
public enum BunnyVideoObjectStatus
{
    Created = 0,
    Uploaded = 1,
    Processing = 2,
    Transcoding = 3,
    Finished = 4,
    Error = 5,
    UploadFailed = 6,
    JitSegmenting = 7,
    JitPlaylistsCreated = 8
}
