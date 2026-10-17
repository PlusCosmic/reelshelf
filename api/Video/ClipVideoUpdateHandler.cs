using Reelshelf.ApexLegends.LegendDetection;
using Reelshelf.Bunny.Models;
using Reelshelf.ClipTranscription;
using Reelshelf.Core;

namespace Reelshelf.Video;

/// <summary>
/// Runs when the video host says a video changed (Bunny's webhook today): re-reads the video from the host, copies
/// it onto its clip, and queues legend detection and transcription once it has finished encoding. The caller
/// authenticates the notification; this trusts only what the host itself reports.
/// </summary>
public class ClipVideoUpdateHandler(
    IClipVideoHost videoHost,
    ClipsStatements clipsStatements,
    LegendDetectionService legendDetectionService,
    ClipTranscriptionService clipTranscriptionService,
    ILogger<ClipVideoUpdateHandler> logger)
{
    public async Task HandleAsync(Guid videoId)
    {
        logger.LogInformation("[VIDEO UPDATE] Looking up clip by VideoId: {VideoId}", videoId);
        ClipsStatements.ClipRow? clip = await clipsStatements.GetClipByVideoId(videoId);
        if (clip == null)
        {
            logger.LogWarning("[VIDEO UPDATE] No clip found for VideoId: {VideoId} - ignoring", videoId);
            return;
        }

        logger.LogInformation("[VIDEO UPDATE] Found clip - ClipId: {ClipId}, Title: {Title}",
            clip.Id, clip.Title);

        logger.LogInformation("[VIDEO UPDATE] Fetching video metadata from the video host for VideoId: {VideoId}", videoId);
        HostedVideoInfo? video = await videoHost.GetVideoAsync(videoId);
        if (video == null)
        {
            logger.LogWarning("[VIDEO UPDATE] Failed to fetch video from the video host for VideoId: {VideoId}", videoId);
            return;
        }

        logger.LogInformation(
            "[VIDEO UPDATE] Fetched video - Title: {Title}, Length: {Length}s, Status: {Status}, EncodeProgress: {Progress}%",
            video.Title, video.LengthSeconds, video.Status, video.EncodeProgress);

        try
        {
            await clipsStatements.UpdateClipMetadata(clip.Id, clip.Title ?? video.Title, video);

            logger.LogInformation("[VIDEO UPDATE] Successfully updated clip metadata for ClipId: {ClipId}", clip.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[VIDEO UPDATE] Error updating clip metadata for ClipId: {ClipId}", clip.Id);
        }

        if (video.Status == (int)BunnyVideoObjectStatus.Finished)
        {
            try
            {
                await legendDetectionService.QueueForEncodedClipAsync(clip);
            }
            catch (Exception ex)
            {
                // Detection is optional; failing to queue it must not make the host retry its notification.
                logger.LogError(ex, "[VIDEO UPDATE] Error queueing legend detection for ClipId: {ClipId}", clip.Id);
            }

            try
            {
                await clipTranscriptionService.QueueForEncodedClipAsync(clip, video.Status);
            }
            catch (Exception ex)
            {
                // Transcription is optional too; failing to queue it must not make the host retry its notification.
                logger.LogError(ex, "[VIDEO UPDATE] Error queueing transcription for ClipId: {ClipId}", clip.Id);
            }
        }
    }
}
