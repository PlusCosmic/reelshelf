using Amazon;
using Amazon.CloudFront;
using Amazon.CloudFront.Model;
using Reelshelf.Video.R2;

namespace Reelshelf.Moderation;

/// <summary>
/// Purges a removed clip from a media domain served by CloudFront (<c>Moderation:CloudFront:DistributionId</c>) with an
/// invalidation, signed with the object store's own credentials, which may create invalidations on that distribution
/// and nothing else in CloudFront. CloudFront accepts an invalidation and then works through the edges on its own, as
/// Cloudflare's purge does, so an accepted invalidation counts as <see cref="CdnPurgeResults.Purged"/>; anything it
/// refuses, throttling (<c>TooManyInvalidationsInProgress</c>) included, throws, and the evidence processor retries.
/// </summary>
public sealed class CloudFrontCdnPurger : ICdnPurger, IDisposable
{
    private readonly IAmazonCloudFront _client;
    private readonly string _distributionId;

    public CloudFrontCdnPurger(R2Options store, string distributionId)
        : this(new AmazonCloudFrontClient(R2Storage.Credentials(store), ClientConfig()), distributionId)
    {
    }

    /// <summary>With a client of the caller's, which tests use to stand in for CloudFront.</summary>
    internal CloudFrontCdnPurger(IAmazonCloudFront client, string distributionId)
    {
        _client = client;
        _distributionId = distributionId;
    }

    /// <summary>
    /// CloudFront's API has one global endpoint, <c>cloudfront.amazonaws.com</c>, signed for <c>us-east-1</c>, whatever
    /// region the store is in. The endpoint comes from that alone, never from <c>AWS_ENDPOINT_URL*</c> or a shared
    /// config file, as for the store's own client.
    /// </summary>
    internal static AmazonCloudFrontConfig ClientConfig() => new()
    {
        RegionEndpoint = RegionEndpoint.USEast1,
        IgnoreConfiguredEndpointUrls = true
    };

    /// <summary>
    /// Every path a clip can be served from. A path ending in <c>*</c> counts as one path however many objects it
    /// matches, and matches their query-string variants too, so <c>clips/{clipId}/*</c> covers every version (any
    /// <c>maxVersion</c>, and a later one a worker uploaded after removal) in one path, where Cloudflare needs each URL.
    /// <c>uploads/{clipId}</c> is never handed out, but it sits under the same origin, so it goes too. Clip ids are
    /// GUIDs, so the paths need no encoding and the upload's wildcard matches that key alone.
    /// </summary>
    public static IReadOnlyList<string> ClipPaths(Guid clipId) =>
        [$"/{R2ObjectKeys.ClipPrefix(clipId)}*", $"/{R2ObjectKeys.Upload(clipId)}*"];

    /// <summary>
    /// CloudFront keeps an invalidation's <c>CallerReference</c>: the same reference with the same paths answers the
    /// invalidation already made instead of making another, and with other paths it is refused. Each call gets a fresh
    /// reference, so the later sweep, or an attempt after a failure, makes a real new invalidation (a worker may have
    /// uploaded, and an edge cached, something since), while the SDK's own retries of one call resend the same request,
    /// so one whose answer was lost finds the invalidation it made rather than failing or making two.
    /// </summary>
    public async Task<string> PurgeClipAsync(Guid clipId, int maxVersion, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> paths = ClipPaths(clipId);
        CreateInvalidationRequest request = new()
        {
            DistributionId = _distributionId,
            InvalidationBatch = new InvalidationBatch
            {
                CallerReference = $"{clipId:N}-{Guid.NewGuid():N}",
                Paths = new Paths { Quantity = paths.Count, Items = [.. paths] }
            }
        };

        try
        {
            await _client.CreateInvalidationAsync(request, cancellationToken);
        }
        catch (AmazonCloudFrontException error)
        {
            throw new InvalidOperationException(
                $"CloudFront refused the invalidation of clip {clipId} on distribution {_distributionId} " +
                $"({(int)error.StatusCode} {error.ErrorCode}): {error.Message}", error);
        }

        return CdnPurgeResults.Purged;
    }

    public void Dispose() => _client.Dispose();
}
