namespace Reelshelf.Video;

/// <summary>
/// Where and how the browser uploads a clip's bytes into a video <see cref="IClipVideoHost.CreateVideoAsync"/> made,
/// without naming the host: the client sends the file to <paramref name="Endpoint"/> with these headers and metadata
/// and adds only what describes the file itself (<c>filename</c>, <c>filetype</c>).
/// </summary>
/// <param name="Protocol">The upload protocol: <see cref="Tus"/> (Bunny) or <see cref="S3Multipart"/> (R2).</param>
/// <param name="Endpoint">
/// The URL uploads are created at. For <see cref="S3Multipart"/>, the API path of the clip's upload, under which the
/// browser asks for part URLs, lists its parts and completes it (<see cref="MultipartUploadEndpoints"/>).
/// </param>
/// <param name="Headers">Request headers to send with every upload request, typically the host's authorisation.</param>
/// <param name="Metadata">Upload metadata the host needs to file the video.</param>
/// <param name="ExpiresAt">When the headers stop authorising the upload.</param>
/// <param name="Multipart">The multipart upload the API started, for <see cref="S3Multipart"/> only.</param>
public sealed record UploadTarget(
    string Protocol,
    string Endpoint,
    Dictionary<string, string> Headers,
    Dictionary<string, string> Metadata,
    DateTimeOffset ExpiresAt,
    MultipartUploadInfo? Multipart = null)
{
    /// <summary>A resumable upload (<see href="https://tus.io/protocols/resumable-upload"/>).</summary>
    public const string Tus = "tus";

    /// <summary>
    /// A presigned S3 multipart upload (R2): the API has started the upload (<see cref="Multipart"/>), the browser
    /// PUTs <see cref="MultipartUploadInfo.PartSize"/> slices of the file to part URLs the API presigns in batches,
    /// and the API completes, checks and queues it (<see cref="MultipartUploadEndpoints"/>).
    /// </summary>
    public const string S3Multipart = "s3-multipart";
}
