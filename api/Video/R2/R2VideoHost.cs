using Reelshelf.Core;
using Reelshelf.Games;
using Reelshelf.Video.Transcoding;

namespace Reelshelf.Video.R2;

/// <summary>
/// Our own pipeline as a clip video host: the bytes in Cloudflare R2 (<see cref="R2Storage"/>), encoded by our
/// transcode workers through <see cref="TranscodeQueue"/>. An R2 clip's video id is its clip id, and R2 has no video
/// object or container: the clip row is the video, kept current by the queue's reports, and its objects are found by
/// key (<see cref="R2ObjectKeys"/>).
/// </summary>
public sealed class R2VideoHost(
    R2Storage storage,
    ClipsStatements clips,
    TranscodeStatements transcodeStatements,
    TranscodeQueue queue,
    TimeProvider timeProvider,
    ILogger<R2VideoHost> logger) : IClipVideoHost
{
    public const string SourceContentType = "application/octet-stream";

    /// <summary>Reserves the clip's keys, which takes no call to R2: the video id is the clip id.</summary>
    public Task<HostedVideo> CreateVideoAsync(Guid clipId, Guid ownerId, GameCategory gameCategory, string title)
    {
        HostedVideoInfo info = new(
            title,
            LengthSeconds: 0,
            ClipVideoStatus.AwaitingUpload,
            EncodeProgress: 0,
            StorageSize: 0,
            timeProvider.GetUtcNow(),
            R2ObjectKeys.Thumbnail,
            HostStatus: null);
        return Task.FromResult(new HostedVideo(clipId, ContainerId: Guid.Empty, info));
    }

    /// <summary>
    /// Browser uploads to R2 are presigned multipart uploads, whose endpoints arrive with #213. Until then the target
    /// names the protocol only, and the uploader refuses a protocol it doesn't speak with a clear message.
    /// </summary>
    public UploadTarget CreateUploadTarget(HostedVideo video, string title)
    {
        return new UploadTarget(UploadTarget.S3Multipart, Endpoint: "", [], [], timeProvider.GetUtcNow());
    }

    /// <summary>
    /// Streams the source into <c>uploads/{clipId}</c>, then marks the clip uploaded and queues its transcode in one
    /// transaction, so an uploaded clip always has a transcode on the way. A failed upload marks the clip
    /// <c>upload_failed</c> before rethrowing.
    /// </summary>
    public async Task UploadAsync(Guid videoId, Stream content, long? contentLength, CancellationToken cancellationToken)
    {
        string key = R2ObjectKeys.Upload(videoId);
        try
        {
            await storage.UploadAsync(key, content, SourceContentType, cancellationToken);
        }
        catch
        {
            try
            {
                await clips.SetUploadFailed(videoId);
            }
            catch (Exception markError)
            {
                logger.LogWarning(markError, "Could not mark clip {ClipId} upload_failed", videoId);
            }

            throw;
        }

        Guid? runId = await transcodeStatements.InTransactionAsync(async () =>
            await clips.SetSourceUploaded(videoId)
                ? await queue.EnqueueAsync(videoId, TranscodeRunTypes.Transcode, new TranscodeRunInput(SourceKey: key))
                : null);
        if (runId is null)
        {
            logger.LogWarning("Clip {ClipId} was not waiting for its upload; no transcode was queued", videoId);
        }
    }

    /// <summary>
    /// The clip row as the video: the queue writes every worker report onto it in the same transaction as the
    /// <c>transcode_run</c> row (processing and its progress, ready, encode_failed), so it is never behind the run.
    /// Null only when there is no such clip.
    /// </summary>
    public async Task<HostedVideoInfo?> GetVideoAsync(Guid videoId)
    {
        ClipsStatements.ClipRow? clip = await clips.GetClipById(videoId);
        if (clip is null)
        {
            return null;
        }

        return new HostedVideoInfo(
            clip.Title ?? "",
            clip.Length ?? 0,
            clip.VideoStatus,
            clip.EncodeProgress ?? 0,
            clip.StorageSize ?? 0,
            clip.DateUploaded ?? clip.CreatedAt,
            R2ObjectKeys.Thumbnail,
            HostStatus: null);
    }

    /// <summary>The worker's reports reach the clip row directly; there is nothing at R2 to poll.</summary>
    public bool NeedsStatusPolling => false;

    /// <summary>Removes the source upload and every version under <c>clips/{clipId}/</c>. Idempotent.</summary>
    public async Task DeleteVideoAsync(Guid videoId)
    {
        await storage.DeleteObjectsAsync([R2ObjectKeys.Upload(videoId)]);
        await storage.DeletePrefixAsync(R2ObjectKeys.ClipPrefix(videoId));
    }

    /// <summary>R2 files clips by key, not in containers, so there are none to delete.</summary>
    public Task DeleteContainerAsync(Guid containerId) => Task.CompletedTask;
}
