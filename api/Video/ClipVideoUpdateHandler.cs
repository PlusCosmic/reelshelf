using Reelshelf.ApexLegends.LegendDetection;
using Reelshelf.ClipTranscription;
using Reelshelf.Core;

namespace Reelshelf.Video;

/// <summary>
/// Applies our own transcode worker's results, which <see cref="Transcoding.TranscodeQueue"/> hands it, to the clip,
/// and queues legend detection and transcription once it has finished encoding.
/// </summary>
public class ClipVideoUpdateHandler(
    ClipsStatements clipsStatements,
    LegendDetectionService legendDetectionService,
    ClipTranscriptionService clipTranscriptionService,
    ILogger<ClipVideoUpdateHandler> logger)
{
    // The worker's report is the source of truth, so these write what it reported. The clip updates run inside the
    // queue's transaction; the follow-ups are queued after it commits.

    /// <summary>A transcode is under way: the clip is processing, at <paramref name="encodeProgress"/> percent.</summary>
    public Task ApplyTranscodeProgressAsync(Guid clipId, int encodeProgress) =>
        clipsStatements.SetTranscodeProgress(clipId, encodeProgress);

    /// <summary>
    /// A transcode or trim finished: the clip is ready on <paramref name="videoVersion"/>, with the output's length,
    /// size, codecs and height. Returns the version it was on before, whose files the caller deletes when it differs;
    /// null when the job was superseded (the clip is no longer on R2, or already on a later version) and nothing moved.
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
    /// A transcode was handed back by a worker that was shutting down: a processing clip is back to <c>uploaded</c>
    /// until another worker claims it.
    /// </summary>
    public Task ApplyTranscodeReleasedAsync(Guid clipId) => clipsStatements.SetTranscodeReleased(clipId);

    /// <summary>A transcode failed for good: the clip is <c>encode_failed</c>, unless it already had a ready video.</summary>
    public Task ApplyTranscodeFailedAsync(Guid clipId) => clipsStatements.SetTranscodeFailed(clipId);

    /// <summary>
    /// Queues legend detection and transcription for a clip whose transcode finished.
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
