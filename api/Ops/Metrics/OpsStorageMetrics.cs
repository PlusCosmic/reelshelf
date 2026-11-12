using System.Net;
using System.Text.RegularExpressions;
using Amazon.CloudWatch;
using Amazon.CloudWatch.Model;
using Amazon.Runtime;
using Reelshelf.Video.R2;

namespace Reelshelf.Ops.Metrics;

/// <summary>What CloudWatch said about the bucket and the CDN, kept for <see cref="OpsStorageMetrics.Lifetime"/>.</summary>
public sealed record StorageMetricsSnapshot(OpsBucketMetrics Bucket, OpsCdnMetrics Cdn);

/// <summary>
/// The bucket's own numbers and the media domain's traffic from CloudWatch, for the monitor's Storage section, read
/// with the object store's AWS keys only when the store is AWS S3 (<c>Storage:Region</c> without <c>Storage:Endpoint</c>).
/// S3's daily storage metrics (<c>BucketSizeBytes</c> by storage class, <c>NumberOfObjects</c>) are free and come from
/// the bucket's region; CloudFront's (requests, bytes downloaded, 4xx and 5xx rates) live in <c>us-east-1</c>. Both
/// change slowly, so a read is kept an hour, and a failed one five minutes, so a permission once granted shows soon.
/// A refusal is answered as <c>denied</c> with the permission needed, never as an error from the endpoint.
/// </summary>
public sealed partial class OpsStorageMetrics(
    R2Options? store,
    ICloudWatchMetricsClient? client,
    string? distributionId,
    OpsSnapshotCache cache,
    TimeProvider timeProvider,
    ILogger<OpsStorageMetrics> logger)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);
    public static readonly TimeSpan FailedLifetime = TimeSpan.FromMinutes(5);

    /// <summary>The only permission the store's keys need for all of this.</summary>
    public const string Permission = "cloudwatch:GetMetricData";

    public const int BucketDays = 30;
    public const int CdnDays = 7;

    /// <summary>CloudFront publishes its metrics in us-east-1 whatever the distribution serves.</summary>
    public const string CloudFrontRegion = "us-east-1";

    private const string ObjectsId = "objects";
    private const string SizesId = "sizes";
    private const int DaySeconds = 86_400;
    private const int HourSeconds = 3_600;

    private static readonly string[] Ok = [OpsMetricStatuses.Ok, OpsMetricStatuses.NotConfigured, OpsMetricStatuses.Unavailable];

    /// <summary>Whether the store is one CloudWatch watches, so its daily totals are worth recording.</summary>
    public bool WatchesBucket => store is not null && store.ServiceUrl is null;

    public Task<StorageMetricsSnapshot> GetAsync(CancellationToken cancellationToken) =>
        cache.GetAsync("storage-metrics",
            snapshot => Ok.Contains(snapshot.Bucket.Status) && Ok.Contains(snapshot.Cdn.Status) ? Lifetime : FailedLifetime,
            LoadAsync, cancellationToken);

    private async Task<StorageMetricsSnapshot> LoadAsync(CancellationToken cancellationToken)
    {
        Task<OpsBucketMetrics> bucket = ReadBucketAsync(cancellationToken);
        Task<OpsCdnMetrics> cdn = ReadCdnAsync(cancellationToken);
        return new StorageMetricsSnapshot(await bucket, await cdn);
    }

    private async Task<OpsBucketMetrics> ReadBucketAsync(CancellationToken cancellationToken)
    {
        if (store is null)
        {
            return Bucket(OpsMetricStatuses.NotConfigured, "No object store is configured.");
        }

        if (store.ServiceUrl is not null || client is null)
        {
            return Bucket(OpsMetricStatuses.Unavailable,
                "The object store is an S3-compatible endpoint, which CloudWatch doesn't watch.");
        }

        if (!BucketNamePattern().IsMatch(store.Bucket))
        {
            return Bucket(OpsMetricStatuses.Failed, "The bucket's name can't be searched for.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        GetMetricDataRequest request = new()
        {
            StartTime = now.UtcDateTime.Date.AddDays(-BucketDays),
            EndTime = now.UtcDateTime,
            ScanBy = ScanBy.TimestampAscending,
            MetricDataQueries =
            [
                new MetricDataQuery
                {
                    Id = SizesId,
                    // Every storage class the bucket holds, found by the search (which GetMetricData alone allows),
                    // each labelled by its class.
                    Expression = "SEARCH('{AWS/S3,BucketName,StorageType} MetricName=\"BucketSizeBytes\" " +
                                 $"BucketName=\"{store.Bucket}\"', 'Average', {DaySeconds})",
                    Label = "${PROP('Dim.StorageType')}",
                    ReturnData = true
                },
                Stat(ObjectsId, "AWS/S3", "NumberOfObjects", "Average", DaySeconds,
                    ("BucketName", store.Bucket), ("StorageType", "AllStorageTypes"))
            ]
        };

        try
        {
            List<Series> series = await ReadAllAsync(store.Region, request, cancellationToken);
            List<OpsStorageClassSeries> sizes = series.Where(s => s.Id != ObjectsId)
                .GroupBy(s => string.IsNullOrWhiteSpace(s.Label) ? "StandardStorage" : s.Label)
                .Select(group => new OpsStorageClassSeries(group.Key, Merge(group.SelectMany(s => s.Points))))
                .OrderByDescending(s => s.Points.LastOrDefault()?.Value ?? 0)
                .ThenBy(s => s.StorageType, StringComparer.Ordinal)
                .ToList();
            List<OpsMetricPoint> objects = Merge(series.Where(s => s.Id == ObjectsId).SelectMany(s => s.Points));
            return new OpsBucketMetrics(OpsMetricStatuses.Ok, null, null, now, sizes, objects, null);
        }
        catch (Exception ex) when (Refusal(ex, cancellationToken) is { } refusal)
        {
            logger.LogWarning(ex, "Reading the bucket's storage metrics from CloudWatch failed: {Status}", refusal.Status);
            return Bucket(refusal.Status, refusal.Error, refusal.Permission, now);
        }
    }

    private async Task<OpsCdnMetrics> ReadCdnAsync(CancellationToken cancellationToken)
    {
        string? distribution = string.IsNullOrWhiteSpace(distributionId) ? null : distributionId.Trim();
        if (distribution is null)
        {
            return Cdn(OpsMetricStatuses.NotConfigured, null,
                "No CloudFront distribution is set (Moderation:CloudFront:DistributionId).");
        }

        if (store is null || store.ServiceUrl is not null || client is null)
        {
            return Cdn(OpsMetricStatuses.Unavailable, distribution,
                "CloudFront is read with the object store's AWS keys, and the store isn't AWS S3.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        DateTime hour = new(now.UtcDateTime.Ticks - now.UtcDateTime.Ticks % TimeSpan.TicksPerHour, DateTimeKind.Utc);
        (string Name, string Value)[] dimensions = [("DistributionId", distribution), ("Region", "Global")];
        GetMetricDataRequest request = new()
        {
            StartTime = hour.AddDays(-CdnDays),
            EndTime = now.UtcDateTime,
            ScanBy = ScanBy.TimestampAscending,
            MetricDataQueries =
            [
                Stat("requests", "AWS/CloudFront", "Requests", "Sum", HourSeconds, dimensions),
                Stat("bytes", "AWS/CloudFront", "BytesDownloaded", "Sum", HourSeconds, dimensions),
                Stat("client_errors", "AWS/CloudFront", "4xxErrorRate", "Average", HourSeconds, dimensions),
                Stat("server_errors", "AWS/CloudFront", "5xxErrorRate", "Average", HourSeconds, dimensions)
            ]
        };

        try
        {
            List<Series> series = await ReadAllAsync(CloudFrontRegion, request, cancellationToken);
            List<OpsMetricPoint> Of(string id) => Merge(series.Where(s => s.Id == id).SelectMany(s => s.Points));
            List<OpsMetricPoint> requests = Of("requests");
            List<OpsMetricPoint> serverErrors = Of("server_errors");
            return new OpsCdnMetrics(OpsMetricStatuses.Ok, null, null, distribution, now, requests, Of("bytes"),
                Of("client_errors"), serverErrors, WeightedRate(serverErrors, requests, now - TimeSpan.FromDays(1)));
        }
        catch (Exception ex) when (Refusal(ex, cancellationToken) is { } refusal)
        {
            logger.LogWarning(ex, "Reading CloudFront's metrics from CloudWatch failed: {Status}", refusal.Status);
            return Cdn(refusal.Status, distribution, refusal.Error, refusal.Permission, now);
        }
    }

    /// <summary>
    /// The rate over the hours since <paramref name="since"/>, each hour weighted by its requests, so a quiet hour's
    /// one failure doesn't count like a busy hour's.
    /// </summary>
    internal static double? WeightedRate(List<OpsMetricPoint> rates, List<OpsMetricPoint> requests, DateTimeOffset since)
    {
        Dictionary<DateTimeOffset, double> byHour = requests.ToDictionary(p => p.At, p => p.Value);
        double weighted = 0;
        double total = 0;
        foreach (OpsMetricPoint rate in rates.Where(p => p.At >= since))
        {
            if (byHour.TryGetValue(rate.At, out double count) && count > 0)
            {
                weighted += rate.Value * count;
                total += count;
            }
        }

        return total > 0 ? weighted / total : null;
    }

    /// <summary>
    /// The bucket's latest day against the database: its size less the database's total recorded that day (else now),
    /// each day both are known, and how much the difference grew over the week before the latest recorded day.
    /// </summary>
    internal static OpsStorageGap? Gap(IReadOnlyList<OpsStorageClassSeries> sizes, long databaseBytes,
        IReadOnlyDictionary<DateOnly, long> recorded)
    {
        SortedDictionary<DateOnly, long> bucket = new();
        foreach (OpsMetricPoint point in sizes.SelectMany(s => s.Points))
        {
            DateOnly day = DateOnly.FromDateTime(point.At.UtcDateTime);
            bucket[day] = bucket.GetValueOrDefault(day) + (long)Math.Round(point.Value);
        }

        if (bucket.Count == 0)
        {
            return null;
        }

        (DateOnly latestDay, long latestBytes) = (bucket.Keys.Last(), bucket.Values.Last());
        List<OpsStorageGapDay> days = bucket
            .Where(day => recorded.ContainsKey(day.Key))
            .Select(day => new OpsStorageGapDay(day.Key, recorded[day.Key], day.Value, day.Value - recorded[day.Key]))
            .ToList();
        long? growth = null;
        if (days.Count > 1)
        {
            OpsStorageGapDay last = days[^1];
            OpsStorageGapDay? from = days.FirstOrDefault(day => day.Day >= last.Day.AddDays(-7) && day.Day < last.Day);
            growth = from is null ? null : last.DifferenceBytes - from.DifferenceBytes;
        }

        long databaseThatDay = recorded.TryGetValue(latestDay, out long sampled) ? sampled : databaseBytes;
        return new OpsStorageGap(databaseBytes, latestBytes, latestDay, latestBytes - databaseThatDay, growth, days);
    }

    private sealed record Series(string Id, string Label, List<OpsMetricPoint> Points);

    /// <summary>Every page of the request, each result's points in time order.</summary>
    private async Task<List<Series>> ReadAllAsync(string region, GetMetricDataRequest request,
        CancellationToken cancellationToken)
    {
        List<Series> series = [];
        string? token = null;
        do
        {
            request.NextToken = token;
            GetMetricDataResponse response = await client!.GetMetricDataAsync(region, request, cancellationToken);
            foreach (MetricDataResult result in response.MetricDataResults ?? [])
            {
                if (result.StatusCode == StatusCode.Forbidden)
                {
                    throw new MetricsRefusedException(result.Messages?.FirstOrDefault()?.Value);
                }

                List<DateTime> times = result.Timestamps ?? [];
                List<double> values = result.Values ?? [];
                series.Add(new Series(result.Id ?? "", result.Label ?? "",
                    times.Zip(values, (at, value) => new OpsMetricPoint(Utc(at), value)).ToList()));
            }

            token = string.IsNullOrEmpty(response.NextToken) ? null : response.NextToken;
        } while (token is not null);

        return series;
    }

    /// <summary>One series' points from any pages, in time order, the later page winning a repeated time.</summary>
    private static List<OpsMetricPoint> Merge(IEnumerable<OpsMetricPoint> points)
    {
        SortedDictionary<DateTimeOffset, double> byTime = new();
        foreach (OpsMetricPoint point in points)
        {
            byTime[point.At] = point.Value;
        }

        return byTime.Select(pair => new OpsMetricPoint(pair.Key, pair.Value)).ToList();
    }

    private static DateTimeOffset Utc(DateTime at) => at.Kind switch
    {
        DateTimeKind.Local => new DateTimeOffset(at.ToUniversalTime(), TimeSpan.Zero),
        _ => new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc), TimeSpan.Zero)
    };

    private static MetricDataQuery Stat(string id, string ns, string name, string stat, int period,
        params (string Name, string Value)[] dimensions) => new()
    {
        Id = id,
        ReturnData = true,
        MetricStat = new MetricStat
        {
            Period = period,
            Stat = stat,
            Metric = new Amazon.CloudWatch.Model.Metric
            {
                Namespace = ns,
                MetricName = name,
                Dimensions = dimensions.Select(d => new Dimension { Name = d.Name, Value = d.Value }).ToList()
            }
        }
    };

    private sealed record Failure(string Status, string Error, string? Permission);

    /// <summary>
    /// What a failed read means: refused (the keys lack <see cref="Permission"/>), or failed with CloudWatch's words.
    /// Null for the caller's own cancellation, which goes on up.
    /// </summary>
    private static Failure? Refusal(Exception ex, CancellationToken cancellationToken)
    {
        if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        return ex switch
        {
            MetricsRefusedException refused => new Failure(OpsMetricStatuses.Denied,
                refused.Message, Permission),
            AmazonServiceException service when service.StatusCode == HttpStatusCode.Forbidden ||
                                                service.ErrorCode is "AccessDenied" or "AccessDeniedException" =>
                new Failure(OpsMetricStatuses.Denied, service.Message, Permission),
            AmazonServiceException service => new Failure(OpsMetricStatuses.Failed,
                $"{service.ErrorCode ?? service.StatusCode.ToString()}: {service.Message}", null),
            _ => new Failure(OpsMetricStatuses.Failed, ex.Message, null)
        };
    }

    private sealed class MetricsRefusedException(string? message)
        : Exception(message ?? "CloudWatch refused the read.");

    private static OpsBucketMetrics Bucket(string status, string error, string? permission = null,
        DateTimeOffset? readAt = null) =>
        new(status, error, permission, readAt, [], [], null);

    private static OpsCdnMetrics Cdn(string status, string? distribution, string? error, string? permission = null,
        DateTimeOffset? readAt = null) =>
        new(status, error, permission, distribution, readAt, [], [], [], [], null);

    /// <summary>S3's bucket names: nothing that could close the search's quotes.</summary>
    [GeneratedRegex("^[a-z0-9][a-z0-9.-]{1,61}[a-z0-9]$")]
    private static partial Regex BucketNamePattern();
}
