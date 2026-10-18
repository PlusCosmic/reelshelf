using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Reelshelf.Video.R2;

namespace Reelshelf.Test.Video;

/// <summary>
/// An in-memory bucket behind the S3 calls <see cref="R2Storage"/> makes, so tests can run uploads, completions,
/// aborts and deletes without R2. Presigning still signs for real, which needs no network.
/// </summary>
internal sealed class FakeR2Client : AmazonS3Client
{
    public static readonly R2Options Options = new()
    {
        AccountId = "test", AccessKeyId = "id", SecretAccessKey = "secret", Bucket = "bucket",
        MediaBaseUrl = "https://media.example"
    };

    private int _nextUploadId;

    public FakeR2Client() : base(R2Storage.Credentials(Options), R2Storage.ClientConfig(Options))
    {
    }

    /// <summary>Objects by key, with their sizes.</summary>
    public Dictionary<string, long> Objects { get; } = [];

    /// <summary>Open multipart uploads by id.</summary>
    public Dictionary<string, OpenUpload> Uploads { get; } = [];

    public List<string> Aborted { get; } = [];

    public R2Storage Storage() => new(Options, this, NullLogger<R2Storage>.Instance);

    /// <summary>Opens an upload into <paramref name="key"/> as the API would and returns its id.</summary>
    public string Open(string key)
    {
        string uploadId = $"upload-{++_nextUploadId}";
        Uploads[uploadId] = new OpenUpload(key, []);
        return uploadId;
    }

    /// <summary>A part as the browser's PUT leaves it.</summary>
    public void PutPart(string uploadId, int partNumber, long size) =>
        Uploads[uploadId].Parts[partNumber] = new UploadedPartState($"\"etag-{partNumber}\"", size);

    public override Task<InitiateMultipartUploadResponse> InitiateMultipartUploadAsync(
        InitiateMultipartUploadRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new InitiateMultipartUploadResponse { UploadId = Open(request.Key), Key = request.Key });

    public override Task<ListPartsResponse> ListPartsAsync(ListPartsRequest request,
        CancellationToken cancellationToken = default)
    {
        OpenUpload upload = Find(request.UploadId, request.Key);
        return Task.FromResult(new ListPartsResponse
        {
            UploadId = request.UploadId,
            IsTruncated = false,
            Parts = upload.Parts.OrderBy(part => part.Key)
                .Select(part => new PartDetail { PartNumber = part.Key, ETag = part.Value.ETag, Size = part.Value.Size })
                .ToList()
        });
    }

    public override Task<CompleteMultipartUploadResponse> CompleteMultipartUploadAsync(
        CompleteMultipartUploadRequest request, CancellationToken cancellationToken = default)
    {
        OpenUpload upload = Find(request.UploadId, request.Key);
        long size = 0;
        foreach (PartETag part in request.PartETags)
        {
            if (!upload.Parts.TryGetValue(part.PartNumber ?? 0, out UploadedPartState? stored) || stored.ETag != part.ETag)
            {
                throw new AmazonS3Exception("One or more of the specified parts could not be found")
                {
                    ErrorCode = "InvalidPart", StatusCode = HttpStatusCode.BadRequest
                };
            }

            size += stored.Size;
        }

        Uploads.Remove(request.UploadId);
        Objects[request.Key] = size;
        return Task.FromResult(new CompleteMultipartUploadResponse { Key = request.Key });
    }

    public override Task<GetObjectMetadataResponse> GetObjectMetadataAsync(GetObjectMetadataRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!Objects.TryGetValue(request.Key, out long size))
        {
            throw new AmazonS3Exception("Not Found") { StatusCode = HttpStatusCode.NotFound };
        }

        return Task.FromResult(new GetObjectMetadataResponse { ContentLength = size });
    }

    public override Task<ListMultipartUploadsResponse> ListMultipartUploadsAsync(ListMultipartUploadsRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ListMultipartUploadsResponse
        {
            MultipartUploads = Uploads
                .Where(upload => upload.Value.Key.StartsWith(request.Prefix ?? "", StringComparison.Ordinal))
                .Select(upload => new MultipartUpload { Key = upload.Value.Key, UploadId = upload.Key })
                .ToList()
        });

    public override Task<AbortMultipartUploadResponse> AbortMultipartUploadAsync(AbortMultipartUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        Find(request.UploadId, request.Key);
        Uploads.Remove(request.UploadId);
        Aborted.Add(request.UploadId);
        return Task.FromResult(new AbortMultipartUploadResponse());
    }

    public override Task<DeleteObjectResponse> DeleteObjectAsync(string bucketName, string key,
        CancellationToken cancellationToken = default)
    {
        Objects.Remove(key);
        return Task.FromResult(new DeleteObjectResponse());
    }

    public override Task<ListObjectsV2Response> ListObjectsV2Async(ListObjectsV2Request request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ListObjectsV2Response
        {
            IsTruncated = false,
            S3Objects = Objects.Keys.Where(key => key.StartsWith(request.Prefix, StringComparison.Ordinal))
                .Select(key => new S3Object { Key = key, Size = Objects[key] })
                .ToList()
        });

    private OpenUpload Find(string uploadId, string key)
    {
        if (!Uploads.TryGetValue(uploadId, out OpenUpload? upload) || upload.Key != key)
        {
            throw new AmazonS3Exception("The specified upload does not exist")
            {
                ErrorCode = "NoSuchUpload", StatusCode = HttpStatusCode.NotFound
            };
        }

        return upload;
    }

    public sealed record OpenUpload(string Key, Dictionary<int, UploadedPartState> Parts);

    public sealed record UploadedPartState(string ETag, long Size);
}
