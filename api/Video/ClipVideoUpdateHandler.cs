using Reelshelf.ApexLegends.LegendDetection;
using Reelshelf.ClipTranscription;
using Reelshelf.Core;

namespace Reelshelf.Video;

/// <summary>
/// Runs when the video host says a video changed (Bunny's webhook today): re-reads the video from the host, copies
/// it onto its clip, and queues legend detection and transcription once it has finished encoding. The caller
/// authenticates the notification; this trusts only what the host itself reports. It also applies our own
/// transcode worker's results, which <see cref="Transcoding.TranscodeQueue"/> hands it.
/// </summary>
public class ClipVideoUpdateHandler(
    ClipVideoHosts videoHosts,
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
        HostedVideoInfo? video = await videoHosts.For(clip.VideoHost).GetVideoAsync(videoId);
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
            await clipsStatements.UpdateClipMetadata(clip.Id, clip.VideoHost, clip.VideoId, clip.Title ?? video.Title, video);

            logger.LogInformation("[VIDEO UPDATE] Successfully updated clip metadata for ClipId: {ClipId}", clip.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[VIDEO UPDATE] Error updating clip metadata for ClipId: {ClipId}", clip.Id);
        }

        if (video.Status.IsPlayable())
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

    // Results from our own transcode worker (Transcoding/TranscodeQueue). Unlike Bunny's webhook, the worker's
    // report is the source of truth, so these write what it reported rather than re-reading a host. The clip
    // updates run inside the queue's transaction; the follow-ups are queued after it commits.

    /// <summary>A transcode is under way: the clip is processing, at <paramref name="encodeProgress"/> percent.</summary>
    public Task ApplyTranscodeProgressAsync(Guid clipId, int encodeProgress) =>
        clipsStatements.SetTranscodeProgress(clipId, encodeProgress);

    /// <summary>
    /// A transcode or trim finished: the clip is ready on <paramref name="videoVersion"/>, with the output's length,
    /// size, codecs and height. Returns the version it was on before (null when there is no such clip), whose files
    /// the caller deletes when it differs.
    /// </summary>
    public Task<int?> ApplyTranscodeCompletedAsync(
        Guid clipId,
        int videoVersion,
        int lengthSeconds,
        long storageSize,
        string? codecs,
        int? height) =>
        clipsStatements.SetTranscodeReady(clipId, videoVersion, lengthSeconds, storageSize, codecs, height);

    /// <summary>
    /// A <c>migrate</c> job finished: the clip moves from Bunny to R2 on the job's version and is ready there
    /// (<see cref="ClipsStatements.SetMigratedToR2"/>). Nothing is queued after it: legend detection, transcripts and
    /// summaries are keyed by clip and stay. False when the clip is no longer on Bunny, so nothing moved.
    /// </summary>
    public Task<bool> ApplyMigrationCompletedAsync(
        Guid clipId,
        int videoVersion,
        int lengthSeconds,
        long storageSize,
        string? codecs,
        int? height) =>
        clipsStatements.SetMigratedToR2(clipId, videoVersion, lengthSeconds, storageSize, codecs, height);

    /// <summary>
    /// A transcode was handed back by a worker that was shutting down: a processing clip is back to <c>uploaded</c>
    /// until another worker claims it.
    /// </summary>
    public Task ApplyTranscodeReleasedAsync(Guid clipId) => clipsStatements.SetTranscodeReleased(clipId);

    /// <summary>A transcode failed for good: the clip is <c>encode_failed</c>, unless it already had a ready video.</summary>
    public Task ApplyTranscodeFailedAsync(Guid clipId) => clipsStatements.SetTranscodeFailed(clipId);

    /// <summary>
    /// Queues legend detection and transcription for a clip whose transcode finished, as the webhook path does.
    /// Safe to repeat: each clip gets one automatic run of each.
    /// </summary>
    public async Task QueueEncodedFollowUpsAsync(Guid clipId)
    {
        ClipsStatements.ClipRow? clip = await clipsStatements.GetClipById(clipId);
        if (clip is null || !clip.VideoStatus.IsPlayable())
        {
            return;
        }

        await QueueLegendDetectionAsync(clip);
        try
        {
            await clipTranscriptionService.QueueForEncodedClipAsync(clip, clip.VideoStatus);
        }
        catch (Exception ex)
        {
            // Transcription is optional; failing to queue it must not fail the worker's report.
            logger.LogError(ex, "[VIDEO UPDATE] Error queueing transcription for ClipId: {ClipId}", clip.Id);
        }
    }

    /// <summary>
    /// Queues legend detection as soon as a transcode reports its thumbnails, before the encode finishes
    /// (<see cref="Transcoding.TranscodingOptions.QueueDetectionOnThumbnails"/>). Detection needs only the frames.
    /// </summary>
    public async Task QueueDetectionOnThumbnailsAsync(Guid clipId)
    {
        ClipsStatements.ClipRow? clip = await clipsStatements.GetClipById(clipId);
        if (clip is not null)
        {
            await QueueLegendDetectionAsync(clip);
        }
    }

    private async Task QueueLegendDetectionAsync(ClipsStatements.ClipRow clip)
    {
        try
        {
            await legendDetectionService.QueueForEncodedClipAsync(clip);
        }
        catch (Exception ex)
        {
            // Detection is optional; failing to queue it must not fail the worker's report.
            logger.LogError(ex, "[VIDEO UPDATE] Error queueing legend detection for ClipId: {ClipId}", clip.Id);
        }
    }
}
