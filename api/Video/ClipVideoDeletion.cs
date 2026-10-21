using Reelshelf.Video.Transcoding;

namespace Reelshelf.Video;

/// <summary>
/// Deletes what a clip's video left at its hosts before the clip row goes: the video at the host it plays from, any
/// R2 copy of a Bunny clip (a <c>migrate</c> job's output, or the versions of a clip switched back to Bunny), and a migrated clip's Bunny copy still waiting out its
/// grace period (<c>bunny_video_id</c>), which the delete sweep can't find once the row is gone. Every step is
/// idempotent (a host that no longer knows a video counts it as deleted), so a failed delete is simply repeated.
/// </summary>
public static class ClipVideoDeletion
{
    public static async Task DeleteAsync(ClipVideoHosts videoHosts, ITranscodeStorage transcodeStorage, Guid clipId,
        string videoHost, Guid videoId, Guid? bunnyVideoId)
    {
        await videoHosts.For(videoHost).DeleteVideoAsync(videoId);
        if (videoHost == VideoHostNames.Bunny)
        {
            // A migrate job may have written the clip's R2 copy already (deleting the clip cascades to the job), and a
            // clip switched back to Bunny keeps the versions it played on R2.
            await transcodeStorage.DeleteAllVersionsAsync(clipId);
        }

        await DeleteBunnyCopyAsync(videoHosts, videoHost, videoId, bunnyVideoId);
    }

    /// <summary>
    /// Deletes a migrated clip's Bunny copy, if it still has one. Nothing to do when the clip was switched back and
    /// plays from that copy, since <paramref name="videoId"/> was just deleted.
    /// </summary>
    public static async Task DeleteBunnyCopyAsync(ClipVideoHosts videoHosts, string videoHost, Guid videoId, Guid? bunnyVideoId)
    {
        if (bunnyVideoId is not { } copy || (videoHost == VideoHostNames.Bunny && copy == videoId))
        {
            return;
        }

        await videoHosts.For(VideoHostNames.Bunny).DeleteVideoAsync(copy);
    }
}
