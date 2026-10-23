using Amazon.S3;
using Reelshelf.Core;
using Reelshelf.Exceptions;
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
    IUploadProbe probe,
    TimeProvider timeProvider,
    ILogger<R2VideoHost> logger) : IClipVideoHost, IMultipartUploadHost
{
    public const string SourceContentType = "application/octet-stream";

    /// <summary>How long a part URL lives. The browser asks for a few at a time, just before it needs them.</summary>
    public static readonly TimeSpan PartUrlLifetime = TimeSpan.FromHours(1);

    /// <summary>How long a browser upload stays open: the bucket's lifecycle rule aborts it after a day.</summary>
    public static readonly TimeSpan UploadLifetime = TimeSpan.FromDays(1);

    /// <summary>The ffprobe at completion reads only the headers, well within this.</summary>
    private static readonly TimeSpan ProbeUrlLifetime = TimeSpan.FromMinutes(10);

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
    /// Starts a multipart upload into <c>uploads/{clipId}</c> for the browser, cut into parts sized for the declared
    /// file (<see cref="MultipartUploadParts"/>). The browser then goes through <see cref="MultipartUploadEndpoints"/>:
    /// part URLs, the parts so far when resuming, and completion.
    /// </summary>
    public async Task<UploadTarget> CreateUploadTargetAsync(HostedVideo video, string title, long fileSize)
    {
        string uploadId = await storage.StartMultipartUploadAsync(
            R2ObjectKeys.Upload(video.VideoId), SourceContentType, CancellationToken.None);
        return new UploadTarget(
            UploadTarget.S3Multipart,
            MultipartUploadEndpoints.UploadPath(video.VideoId),
            [],
            [],
            timeProvider.GetUtcNow() + UploadLifetime,
            MultipartUploadParts.For(uploadId, fileSize));
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

        await QueueTranscodeAsync(videoId);
    }

    public PresignedUploadParts PresignParts(ClipsStatements.ClipRow clip, string uploadId, IReadOnlyList<int> partNumbers)
    {
        RequireUploadId(uploadId);
        RequireAwaitingUpload(clip);
        int partCount = MultipartUploadParts.PartCountFor(DeclaredSize(clip));
        if (partNumbers.Count is 0 or > MultipartUploadEndpoints.MaxPartsPerBatch)
        {
            throw new BadRequestException(
                $"Ask for between 1 and {MultipartUploadEndpoints.MaxPartsPerBatch} parts at a time");
        }

        if (partNumbers.Any(number => number < 1 || number > partCount) || partNumbers.Distinct().Count() != partNumbers.Count)
        {
            throw new BadRequestException($"Part numbers must be distinct, from 1 to {partCount}");
        }

        string key = R2ObjectKeys.Upload(clip.Id);
        return new PresignedUploadParts(
            partNumbers.Select(number =>
                new PresignedUploadPart(number, storage.PresignUploadPart(key, uploadId, number, PartUrlLifetime))).ToList(),
            timeProvider.GetUtcNow() + PartUrlLifetime);
    }

    public async Task<List<UploadedPart>> ListPartsAsync(ClipsStatements.ClipRow clip, string uploadId,
        CancellationToken cancellationToken)
    {
        RequireUploadId(uploadId);
        // 409 once the clip has moved on (completed, or failed), so a browser resuming knows to forget the upload.
        RequireAwaitingUpload(clip);
        return await storage.ListPartsAsync(R2ObjectKeys.Upload(clip.Id), uploadId, cancellationToken)
               ?? throw new NotFoundException("This upload is no longer open");
    }

    /// <summary>
    /// Joins the parts, then checks the object before anything is queued: its size must be the one the clip
    /// reserved (so the quota holds), and a quick ffprobe over a presigned GET must find a video with a size and a
    /// length no longer than the owner's plan keeps. A file that fails is deleted and the clip marked
    /// <c>upload_failed</c> (422). HDR and variable frame
    /// rate are the worker's to handle. A probe that couldn't finish fails with 503 and leaves the clip waiting, and
    /// completing again picks up the joined object rather than the upload, which is gone by then.
    /// </summary>
    public async Task<CompletedUpload> CompleteUploadAsync(ClipsStatements.ClipRow clip, string uploadId,
        IReadOnlyList<CompletedUploadPart> parts, Plans.TierLimits limits, CancellationToken cancellationToken)
    {
        RequireUploadId(uploadId);
        if (clip.VideoStatus.IsUploaded())
        {
            return new CompletedUpload(clip.Id, clip.VideoStatus);
        }

        RequireAwaitingUpload(clip);
        long declaredSize = DeclaredSize(clip);
        RequireEveryPart(parts, MultipartUploadParts.PartCountFor(declaredSize));

        string key = R2ObjectKeys.Upload(clip.Id);
        try
        {
            await storage.CompleteMultipartUploadAsync(key, uploadId, parts, cancellationToken);
        }
        catch (AmazonS3Exception error) when (error.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            // A part that isn't there, an ETag that doesn't match or a part below the minimum size.
            throw new BadRequestException($"The parts don't match the upload ({error.ErrorCode}); list them and try again");
        }

        long uploadedSize = await storage.GetObjectSizeAsync(key, cancellationToken)
                            ?? throw new NotFoundException("This upload is no longer open");

        string? problem = UploadValidation.SizeProblem(declaredSize, uploadedSize);
        if (problem is null)
        {
            try
            {
                UploadProbeResult result = await probe.ProbeAsync(
                    new Uri(storage.PresignGet(key, ProbeUrlLifetime)), cancellationToken);
                problem = UploadValidation.Problem(result) ?? limits.LengthProblem(result.DurationSeconds);
            }
            catch (UnreadableUploadException error)
            {
                logger.LogInformation(error, "Upload for clip {ClipId} is not a readable video", clip.Id);
                problem = "The file couldn't be read as a video.";
            }
            catch (TimeoutException error)
            {
                logger.LogWarning(error, "Could not check the upload for clip {ClipId}", clip.Id);
                throw new ServiceUnavailableException("The upload couldn't be checked just now; try again", error);
            }
        }

        if (problem is not null)
        {
            await RejectUploadAsync(clip.Id, key, problem);
            throw new UnprocessableContentException(problem);
        }

        await QueueTranscodeAsync(clip.Id);
        return new CompletedUpload(clip.Id, ClipVideoStatus.Uploaded);
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

    /// <summary>
    /// Aborts a browser upload still open on the clip (cancel, and the abandoned-upload purge), then removes the
    /// source upload and every version under <c>clips/{clipId}/</c>. Idempotent.
    /// </summary>
    public async Task DeleteVideoAsync(Guid videoId)
    {
        await storage.AbortMultipartUploadsAsync(R2ObjectKeys.Upload(videoId));
        await storage.DeleteObjectsAsync([R2ObjectKeys.Upload(videoId)]);
        await storage.DeletePrefixAsync(R2ObjectKeys.ClipPrefix(videoId));
    }

    /// <summary>R2 files clips by key, not in containers, so there are none to delete.</summary>
    public Task DeleteContainerAsync(Guid containerId) => Task.CompletedTask;

    /// <summary>
    /// Marks the clip uploaded and queues its transcode in one transaction, so an uploaded clip always has a
    /// transcode on the way. Shared by the server-side upload and the browser's completion.
    /// </summary>
    private async Task QueueTranscodeAsync(Guid clipId)
    {
        Guid? runId = await transcodeStatements.InTransactionAsync(async () =>
            await clips.SetSourceUploaded(clipId)
                ? await queue.EnqueueAsync(clipId, TranscodeRunTypes.Transcode,
                    new TranscodeRunInput(SourceKey: R2ObjectKeys.Upload(clipId)))
                : null);
        if (runId is null)
        {
            logger.LogWarning("Clip {ClipId} was not waiting for its upload; no transcode was queued", clipId);
        }
    }

    /// <summary>Marks the clip <c>upload_failed</c> and deletes the file, which never counts toward anything.</summary>
    private async Task RejectUploadAsync(Guid clipId, string key, string problem)
    {
        logger.LogInformation("Rejected the upload for clip {ClipId}: {Problem}", clipId, problem);
        await clips.SetUploadFailed(clipId);
        try
        {
            await storage.DeleteObjectsAsync([key]);
        }
        catch (Exception error)
        {
            // The clip's delete (the uploader's cancel, or the purge a day later) removes it.
            logger.LogWarning(error, "Could not delete the rejected upload of clip {ClipId}", clipId);
        }
    }

    private static void RequireUploadId(string uploadId)
    {
        if (string.IsNullOrWhiteSpace(uploadId) || uploadId.Length > 1024)
        {
            throw new BadRequestException("An upload id is required");
        }
    }

    private static void RequireAwaitingUpload(ClipsStatements.ClipRow clip)
    {
        if (clip.VideoStatus != ClipVideoStatus.AwaitingUpload)
        {
            throw new ConflictException(clip.VideoStatus == ClipVideoStatus.UploadFailed
                ? "This clip's upload failed; upload the file again"
                : "This clip is no longer waiting for its upload");
        }
    }

    /// <summary>The size the clip reserved, which the parts are cut from and the upload must match.</summary>
    private static long DeclaredSize(ClipsStatements.ClipRow clip) =>
        clip.FileSize is { } size and > 0
            ? size
            : throw new ConflictException("This clip has no file size to upload against");

    /// <summary>A completion names every part, 1 to <paramref name="partCount"/>, each once and with its ETag.</summary>
    private static void RequireEveryPart(IReadOnlyList<CompletedUploadPart> parts, int partCount)
    {
        bool complete = parts.Count == partCount
                        && parts.OrderBy(part => part.PartNumber).Select(part => part.PartNumber)
                            .SequenceEqual(Enumerable.Range(1, partCount))
                        && parts.All(part => !string.IsNullOrWhiteSpace(part.Etag));
        if (!complete)
        {
            throw new BadRequestException($"Complete the upload with all {partCount} parts and their ETags");
        }
    }
}
