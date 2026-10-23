using Reelshelf.Core;

namespace Reelshelf.Video;

/// <summary>A multipart upload the API started for a clip's source.</summary>
/// <param name="UploadId">The storage's id for the upload; the browser sends it back with every step.</param>
/// <param name="PartSize">Every part but the last is exactly this many bytes.</param>
/// <param name="PartCount">How many parts the file is cut into (1 to <see cref="MultipartUploadParts.MaxParts"/>).</param>
public sealed record MultipartUploadInfo(string UploadId, long PartSize, int PartCount);

/// <summary>How a file is cut into parts: the same answer for the same size, so nothing about it is stored.</summary>
public static class MultipartUploadParts
{
    /// <summary>S3's limit on parts per upload.</summary>
    public const int MaxParts = 10_000;

    /// <summary>R2 wants every part but the last the same size and at least 5 MiB; 8 MiB keeps retries cheap.</summary>
    public const long MinPartSize = 8L * 1024 * 1024;

    private const long Mebibyte = 1024L * 1024;

    /// <summary>
    /// <see cref="MinPartSize"/>, or for a file that would need more than <see cref="MaxParts"/> of those, the
    /// smallest whole number of MiB that fits it in that many: about 105 MiB for the largest file a clip may declare
    /// (<see cref="Storage.StorageQuota.MaxDeclaredFileSizeBytes"/>), well inside S3's 5 GiB part limit.
    /// </summary>
    public static long PartSizeFor(long fileSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fileSize);
        long needed = (fileSize + MaxParts - 1) / MaxParts;
        long rounded = (needed + Mebibyte - 1) / Mebibyte * Mebibyte;
        return Math.Max(MinPartSize, rounded);
    }

    public static int PartCountFor(long fileSize)
    {
        long partSize = PartSizeFor(fileSize);
        return (int)Math.Max(1, (fileSize + partSize - 1) / partSize);
    }

    public static MultipartUploadInfo For(string uploadId, long fileSize) =>
        new(uploadId, PartSizeFor(fileSize), PartCountFor(fileSize));
}

/// <summary>Asks for PUT URLs for some of an upload's parts.</summary>
/// <param name="PartNumbers">Up to <see cref="MultipartUploadEndpoints.MaxPartsPerBatch"/> part numbers.</param>
public sealed record PresignUploadPartsRequest(string UploadId, List<int> PartNumbers);

public sealed record PresignedUploadPart(int PartNumber, string Url);

/// <summary>Part URLs, each good until <paramref name="ExpiresAt"/>.</summary>
public sealed record PresignedUploadParts(List<PresignedUploadPart> Parts, DateTimeOffset ExpiresAt);

/// <summary>A part the storage holds, with the ETag it answered the PUT with and its size.</summary>
public sealed record UploadedPart(int PartNumber, string Etag, long Size);

public sealed record UploadedParts(List<UploadedPart> Parts);

/// <summary>A part to join into the object, with the ETag its PUT returned.</summary>
public sealed record CompletedUploadPart(int PartNumber, string Etag);

/// <summary>Every part of the file, from 1 up, with their ETags.</summary>
public sealed record CompleteUploadRequest(string UploadId, List<CompletedUploadPart> Parts);

/// <summary>The clip once its upload is complete: <c>uploaded</c> (or further along), with its transcode queued.</summary>
public sealed record CompletedUpload(Guid ClipId, ClipVideoStatus Status);

/// <summary>
/// A host whose browser uploads are presigned multipart uploads (<see cref="UploadTarget.S3Multipart"/>). The
/// endpoints in <see cref="MultipartUploadEndpoints"/> check that the caller owns the clip and hand the rest to it.
/// Each method throws a <see cref="Exceptions.DomainException"/> for anything the caller can act on.
/// </summary>
public interface IMultipartUploadHost
{
    /// <summary>PUT URLs for <paramref name="partNumbers"/> of the clip's upload.</summary>
    PresignedUploadParts PresignParts(ClipsStatements.ClipRow clip, string uploadId, IReadOnlyList<int> partNumbers);

    /// <summary>The parts uploaded so far, for resuming; 404 when the upload is gone.</summary>
    Task<List<UploadedPart>> ListPartsAsync(ClipsStatements.ClipRow clip, string uploadId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Joins the parts, checks the file and queues its transcode. A file that is clearly not a usable video, or is
    /// longer than the owner's plan keeps (<paramref name="limits"/>), marks the clip <c>upload_failed</c> and fails
    /// with 422. Repeating a completion that went through is accepted.
    /// </summary>
    Task<CompletedUpload> CompleteUploadAsync(ClipsStatements.ClipRow clip, string uploadId,
        IReadOnlyList<CompletedUploadPart> parts, Plans.TierLimits limits, CancellationToken cancellationToken);
}
