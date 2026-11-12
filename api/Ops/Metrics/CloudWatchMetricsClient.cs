using System.Collections.Concurrent;
using Amazon;
using Amazon.CloudWatch;
using Amazon.CloudWatch.Model;
using Reelshelf.Video.R2;

namespace Reelshelf.Ops.Metrics;

/// <summary>
/// One <c>GetMetricData</c> page in a region: all the storage numbers need from CloudWatch, so tests stand in for it
/// without the SDK's wire format.
/// </summary>
public interface ICloudWatchMetricsClient
{
    Task<GetMetricDataResponse> GetMetricDataAsync(string region, GetMetricDataRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// CloudWatch signed with the object store's own AWS keys (<c>Storage:AccessKeyId</c>/<c>SecretAccessKey</c>), one
/// client per region: the bucket's for S3's storage metrics, <c>us-east-1</c> for CloudFront's. As for the store's own
/// client, the endpoint comes from the region alone, never from <c>AWS_ENDPOINT_URL*</c> or a shared config file.
/// </summary>
public sealed class AwsCloudWatchMetricsClient(R2Options store) : ICloudWatchMetricsClient, IDisposable
{
    private readonly ConcurrentDictionary<string, AmazonCloudWatchClient> _clients = new();

    public Task<GetMetricDataResponse> GetMetricDataAsync(string region, GetMetricDataRequest request,
        CancellationToken cancellationToken) =>
        _clients.GetOrAdd(region, name => new AmazonCloudWatchClient(R2Storage.Credentials(store),
                new AmazonCloudWatchConfig
                {
                    RegionEndpoint = RegionEndpoint.GetBySystemName(name),
                    IgnoreConfiguredEndpointUrls = true
                }))
            .GetMetricDataAsync(request, cancellationToken);

    public void Dispose()
    {
        foreach (AmazonCloudWatchClient client in _clients.Values)
        {
            client.Dispose();
        }
    }
}
