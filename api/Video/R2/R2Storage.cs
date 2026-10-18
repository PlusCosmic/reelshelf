using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Reelshelf.Video.Transcoding;

namespace Reelshelf.Video.R2;

/// <summary>
/// The R2 bucket, through AWSSDK.S3 and only S3-compatible calls, so moving to S3 later is a copy job and a new
/// endpoint. Presigning needs no network: it signs with the key alone. Payload signing and the SDK's default
/// checksums are off because R2 accepts neither the streaming signature nor every checksum the SDK would send.
/// </summary>
public sealed class R2Storage : ITranscodeStorage, IDisposable
{
    /// <summary>
    /// The size of each part of a server-side multipart upload. R2 wants every part but the last the same size,
    /// and at least 5 MiB.
    /// </summary>
    public const int PartSize = 8 * 1024 * 1024;

    /// <summary>The longest a SigV4 presigned URL may live.</summary>
    public static readonly TimeSpan MaxPresignLifetime = TimeSpan.FromDays(7);

    private readonly R2Options _options;
    private readonly AmazonS3Client _client;
    private readonly ILogger<R2Storage> _logger;

    public R2Storage(R2Options options, ILogger<R2Storage> logger)
    {
        _options = options;
        _logger = logger;
        _client = new AmazonS3Client(
            new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey),
            new AmazonS3Config
            {
                ServiceURL = options.ServiceUrl,
                AuthenticationRegion = R2Options.Region,
                ForcePathStyle = true,
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
            });
    }

    /// <summary>A GET for <paramref name="key"/>.</summary>
    public string PresignGet(string key, TimeSpan lifetime)
    {
        return _client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _options.Bucket,
            Key = key,
            Verb = HttpVerb.GET,
            Protocol = Protocol.HTTPS,
            Expires = ExpiresAt(lifetime)
        });
    }

    /// <summary>
    /// A PUT for <paramref name="key"/> that signs its <c>Content-Type</c> and <c>Cache-Control</c>, so the object is
    /// stored with exactly those and an upload sending anything else is refused.
    /// </summary>
    public string PresignPut(string key, string contentType, string cacheControl, TimeSpan lifetime)
    {
        GetPreSignedUrlRequest request = new()
        {
            BucketName = _options.Bucket,
            Key = key,
            Verb = HttpVerb.PUT,
            Protocol = Protocol.HTTPS,
            ContentType = contentType,
            Expires = ExpiresAt(lifetime)
        };
        request.Headers.CacheControl = cacheControl;
        return _client.GetPreSignedURL(request);
    }

    public TranscodeJobUrls UrlsFor(Guid clipId, string jobType, int videoVersion, int currentVersion, TimeSpan lifetime)
    {
        string? sourceKey = jobType switch
        {
            TranscodeRunTypes.Transcode => R2ObjectKeys.Upload(clipId),
            TranscodeRunTypes.Thumbnails => R2ObjectKeys.Output(clipId, videoVersion, R2ObjectKeys.Av1),
            TranscodeRunTypes.Trim => R2ObjectKeys.Output(clipId, currentVersion, R2ObjectKeys.Av1),
            _ => null
        };
        if (sourceKey is null)
        {
            return TranscodeJobUrls.None;
        }

        Dictionary<string, string> uploads = R2ObjectKeys.OutputsFor(jobType).ToDictionary(
            fileName => fileName,
            fileName => PresignPut(R2ObjectKeys.Output(clipId, videoVersion, fileName),
                R2ObjectKeys.ContentType(fileName), R2ObjectKeys.ImmutableCacheControl, lifetime));
        return new TranscodeJobUrls(PresignGet(sourceKey, lifetime), uploads);
    }

    /// <summary>
    /// Streams <paramref name="content"/> into <paramref name="key"/> without knowing its length: one PUT when it fits
    /// in a part, else a multipart upload of <see cref="PartSize"/> parts, aborted if anything fails (and, failing
    /// that, by the bucket's lifecycle rule a day later).
    /// </summary>
    public async Task UploadAsync(string key, Stream content, string contentType, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[PartSize];
        int read = await content.ReadAtLeastAsync(buffer, PartSize, throwOnEndOfStream: false, cancellationToken);
        if (read < PartSize)
        {
            using MemoryStream body = new(buffer, 0, read, writable: false);
            await _client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _options.Bucket,
                Key = key,
                InputStream = body,
                ContentType = contentType,
                DisablePayloadSigning = true,
                AutoCloseStream = false
            }, cancellationToken);
            return;
        }

        InitiateMultipartUploadResponse upload = await _client.InitiateMultipartUploadAsync(
            new InitiateMultipartUploadRequest { BucketName = _options.Bucket, Key = key, ContentType = contentType },
            cancellationToken);
        try
        {
            List<PartETag> parts = [];
            while (read > 0)
            {
                using MemoryStream part = new(buffer, 0, read, writable: false);
                UploadPartResponse uploaded = await _client.UploadPartAsync(new UploadPartRequest
                {
                    BucketName = _options.Bucket,
                    Key = key,
                    UploadId = upload.UploadId,
                    PartNumber = parts.Count + 1,
                    InputStream = part,
                    PartSize = read,
                    DisablePayloadSigning = true
                }, cancellationToken);
                parts.Add(new PartETag(parts.Count + 1, uploaded.ETag));

                read = await content.ReadAtLeastAsync(buffer, PartSize, throwOnEndOfStream: false, cancellationToken);
            }

            await _client.CompleteMultipartUploadAsync(new CompleteMultipartUploadRequest
            {
                BucketName = _options.Bucket,
                Key = key,
                UploadId = upload.UploadId,
                PartETags = parts
            }, cancellationToken);
        }
        catch
        {
            try
            {
                await _client.AbortMultipartUploadAsync(new AbortMultipartUploadRequest
                {
                    BucketName = _options.Bucket, Key = key, UploadId = upload.UploadId
                }, CancellationToken.None);
            }
            catch (Exception abortError)
            {
                _logger.LogWarning(abortError, "Could not abort the multipart upload of {Key}; the lifecycle rule will", key);
            }

            throw;
        }
    }

    /// <summary>Deletes each key. Idempotent: S3 reports success for a key that doesn't exist.</summary>
    public async Task DeleteObjectsAsync(IEnumerable<string> keys)
    {
        // One request per key rather than DeleteObjects, whose required checksum R2 has not always accepted; a clip
        // has a dozen objects at most.
        foreach (string key in keys)
        {
            await _client.DeleteObjectAsync(_options.Bucket, key);
        }
    }

    /// <summary>Deletes every object under <paramref name="prefix"/>.</summary>
    public async Task DeletePrefixAsync(string prefix)
    {
        string? continuationToken = null;
        do
        {
            ListObjectsV2Response page = await _client.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = _options.Bucket, Prefix = prefix, ContinuationToken = continuationToken
            });
            await DeleteObjectsAsync((page.S3Objects ?? []).Select(item => item.Key));
            continuationToken = page.IsTruncated == true ? page.NextContinuationToken : null;
        } while (continuationToken is not null);
    }

    public Task DeleteSourceAsync(Guid clipId) => DeleteObjectsAsync([R2ObjectKeys.Upload(clipId)]);

    public Task DeleteVersionAsync(Guid clipId, int videoVersion) =>
        DeletePrefixAsync(R2ObjectKeys.VersionFolder(clipId, videoVersion) + "/");

    public void Dispose() => _client.Dispose();

    private static DateTime ExpiresAt(TimeSpan lifetime)
    {
        TimeSpan clamped = lifetime > MaxPresignLifetime ? MaxPresignLifetime : lifetime;
        return DateTime.UtcNow + clamped;
    }
}
