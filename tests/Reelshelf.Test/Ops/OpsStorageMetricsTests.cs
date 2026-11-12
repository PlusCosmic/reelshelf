using System.Net;
using Amazon.CloudWatch;
using Amazon.CloudWatch.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Reelshelf.Ops;
using Reelshelf.Ops.Metrics;
using Reelshelf.Video.R2;
using Xunit;

namespace Reelshelf.Test.Ops;

/// <summary>
/// The bucket's and the CDN's numbers from CloudWatch, with a fake client standing in for it: what is asked of which
/// region, how answers are read, refusals reported rather than thrown, the hour's cache, and the database gap.
/// </summary>
public class OpsStorageMetricsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 15, 20, 0, TimeSpan.Zero);
    private const string Distribution = "E2EXAMPLE123";

    private static readonly R2Options AwsStore = new()
    {
        Region = "eu-north-1",
        AccessKeyId = "AKIAEXAMPLE",
        SecretAccessKey = "very-secret",
        Bucket = "reelshelf-media",
        MediaBaseUrl = "https://media.reelshelf.test"
    };

    private static readonly R2Options CompatibleStore = new()
    {
        Endpoint = "https://objects.example.test",
        AccessKeyId = "key",
        SecretAccessKey = "secret",
        Bucket = "clips",
        MediaBaseUrl = "https://media.example.test"
    };

    [Fact]
    public async Task OnAwsS3_TheBucketIsReadInItsRegion_AndCloudFrontInUsEast1()
    {
        FakeCloudWatch cloudWatch = new(Answer);
        StorageMetricsSnapshot snapshot = await Metrics(AwsStore, cloudWatch, Distribution).GetAsync(CancellationToken.None);

        Assert.Equal(2, cloudWatch.Requests.Count);
        (string bucketRegion, GetMetricDataRequest bucket) = cloudWatch.Requests.Single(r => r.Region == "eu-north-1");
        Assert.Equal(Now.UtcDateTime.Date.AddDays(-30), bucket.StartTime);
        Assert.Equal(Now.UtcDateTime, bucket.EndTime);
        MetricDataQuery search = bucket.MetricDataQueries.Single(q => q.Id == "sizes");
        Assert.Equal(
            "SEARCH('{AWS/S3,BucketName,StorageType} MetricName=\"BucketSizeBytes\" BucketName=\"reelshelf-media\"', 'Average', 86400)",
            search.Expression);
        Assert.Equal("${PROP('Dim.StorageType')}", search.Label);
        MetricStat objects = bucket.MetricDataQueries.Single(q => q.Id == "objects").MetricStat;
        Assert.Equal(("AWS/S3", "NumberOfObjects", 86400, "Average"),
            (objects.Metric.Namespace, objects.Metric.MetricName, objects.Period!.Value, objects.Stat));
        Assert.Equal(["BucketName=reelshelf-media", "StorageType=AllStorageTypes"],
            objects.Metric.Dimensions.Select(d => $"{d.Name}={d.Value}"));
        Assert.Equal("eu-north-1", bucketRegion);

        GetMetricDataRequest cdn = cloudWatch.Requests.Single(r => r.Region == "us-east-1").Request;
        Assert.Equal(new DateTime(2026, 11, 5, 15, 0, 0, DateTimeKind.Utc), cdn.StartTime);
        Assert.Equal(
            ["requests:Requests:Sum", "bytes:BytesDownloaded:Sum", "client_errors:4xxErrorRate:Average",
             "server_errors:5xxErrorRate:Average"],
            cdn.MetricDataQueries.Select(q => $"{q.Id}:{q.MetricStat.Metric.MetricName}:{q.MetricStat.Stat}"));
        Assert.All(cdn.MetricDataQueries, q =>
        {
            Assert.Equal("AWS/CloudFront", q.MetricStat.Metric.Namespace);
            Assert.Equal(3600, q.MetricStat.Period);
            Assert.Equal([$"DistributionId={Distribution}", "Region=Global"],
                q.MetricStat.Metric.Dimensions.Select(d => $"{d.Name}={d.Value}"));
        });

        OpsBucketMetrics b = snapshot.Bucket;
        Assert.Equal(OpsMetricStatuses.Ok, b.Status);
        Assert.Equal(Now, b.ReadAt);
        // Largest class first; each series in time order however CloudWatch sent it.
        Assert.Equal(["StandardStorage", "StandardIAStorage"], b.Sizes.Select(s => s.StorageType));
        Assert.Equal([1_000d, 1_200d], b.Sizes[0].Points.Select(p => p.Value));
        Assert.Equal(new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero), b.Sizes[0].Points[0].At);
        Assert.Equal([40d, 42d], b.Objects.Select(p => p.Value));

        OpsCdnMetrics c = snapshot.Cdn;
        Assert.Equal(OpsMetricStatuses.Ok, c.Status);
        Assert.Equal(Distribution, c.DistributionId);
        Assert.Equal([100d, 900d], c.Requests.Select(p => p.Value));
        Assert.Equal([5_000d, 45_000d], c.BytesDownloaded.Select(p => p.Value));
        Assert.Equal([1d, 2d], c.ClientErrorRate.Select(p => p.Value));
        // 10% of 100 requests and 0% of 900: 1% of the day's, not the hours' 5%.
        Assert.Equal([10d, 0d], c.ServerErrorRate.Select(p => p.Value));
        Assert.Equal(1d, c.ServerErrorRateLastDay!.Value, 6);
    }

    [Fact]
    public async Task PagesAreFollowed_AndTheirPointsJoined()
    {
        int page = 0;
        FakeCloudWatch cloudWatch = new((region, request) =>
        {
            if (region != "eu-north-1")
            {
                return new GetMetricDataResponse { MetricDataResults = [] };
            }

            page++;
            return page == 1
                ? new GetMetricDataResponse
                {
                    NextToken = "next",
                    MetricDataResults = [Result("objects", "", (Day(10), 40))]
                }
                : new GetMetricDataResponse { MetricDataResults = [Result("objects", "", (Day(11), 42))] };
        });

        StorageMetricsSnapshot snapshot = await Metrics(AwsStore, cloudWatch, null).GetAsync(CancellationToken.None);

        Assert.Equal([40d, 42d], snapshot.Bucket.Objects.Select(p => p.Value));
        List<GetMetricDataRequest> bucketPages = cloudWatch.Requests.Where(r => r.Region == "eu-north-1")
            .Select(r => r.Request).ToList();
        Assert.Equal(2, bucketPages.Count);
        Assert.Equal("next", cloudWatch.Tokens[1]);
    }

    [Fact]
    public async Task ARefusal_IsReportedWithThePermissionNeeded_AndTriedAgainSoonerThanAnHour()
    {
        FakeTime time = new(Now);
        int calls = 0;
        FakeCloudWatch cloudWatch = new((_, _) =>
        {
            calls++;
            throw new AmazonCloudWatchException(
                "User: arn:aws:iam::123456789012:user/reelshelf-media is not authorized to perform: cloudwatch:GetMetricData")
            {
                ErrorCode = "AccessDenied",
                StatusCode = HttpStatusCode.Forbidden
            };
        });
        OpsStorageMetrics metrics = Metrics(AwsStore, cloudWatch, Distribution, time);

        StorageMetricsSnapshot snapshot = await metrics.GetAsync(CancellationToken.None);

        foreach ((string status, string? permission, string? error) in new[]
                 {
                     (snapshot.Bucket.Status, snapshot.Bucket.Permission, snapshot.Bucket.Error),
                     (snapshot.Cdn.Status, snapshot.Cdn.Permission, snapshot.Cdn.Error)
                 })
        {
            Assert.Equal(OpsMetricStatuses.Denied, status);
            Assert.Equal("cloudwatch:GetMetricData", permission);
            Assert.Contains("not authorized", error);
        }

        Assert.Empty(snapshot.Bucket.Sizes);
        Assert.Equal(2, calls);

        time.Advance(TimeSpan.FromMinutes(4));
        await metrics.GetAsync(CancellationToken.None);
        Assert.Equal(2, calls);
        time.Advance(TimeSpan.FromMinutes(2));
        await metrics.GetAsync(CancellationToken.None);
        Assert.Equal(4, calls);
    }

    [Fact]
    public async Task AResultCloudWatchForbids_IsARefusalToo()
    {
        FakeCloudWatch cloudWatch = new((_, _) => new GetMetricDataResponse
        {
            MetricDataResults =
            [
                new MetricDataResult
                {
                    Id = "sizes",
                    StatusCode = StatusCode.Forbidden,
                    Messages = [new MessageData { Code = "Forbidden", Value = "Not allowed to search." }]
                }
            ]
        });

        StorageMetricsSnapshot snapshot = await Metrics(AwsStore, cloudWatch, null).GetAsync(CancellationToken.None);

        Assert.Equal(OpsMetricStatuses.Denied, snapshot.Bucket.Status);
        Assert.Equal("Not allowed to search.", snapshot.Bucket.Error);
    }

    [Fact]
    public async Task AnotherFailure_IsReportedInCloudWatchsWords()
    {
        FakeCloudWatch cloudWatch = new((_, _) => throw new AmazonCloudWatchException("Rate exceeded")
        {
            ErrorCode = "Throttling",
            StatusCode = HttpStatusCode.BadRequest
        });

        StorageMetricsSnapshot snapshot = await Metrics(AwsStore, cloudWatch, Distribution).GetAsync(CancellationToken.None);

        Assert.Equal(OpsMetricStatuses.Failed, snapshot.Bucket.Status);
        Assert.Equal("Throttling: Rate exceeded", snapshot.Bucket.Error);
        Assert.Null(snapshot.Bucket.Permission);
        Assert.Equal(OpsMetricStatuses.Failed, snapshot.Cdn.Status);
    }

    [Fact]
    public async Task AGoodRead_IsKeptAnHour()
    {
        FakeTime time = new(Now);
        FakeCloudWatch cloudWatch = new(Answer);
        OpsStorageMetrics metrics = Metrics(AwsStore, cloudWatch, Distribution, time);

        await metrics.GetAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(59));
        await metrics.GetAsync(CancellationToken.None);
        Assert.Equal(2, cloudWatch.Requests.Count);

        time.Advance(TimeSpan.FromMinutes(2));
        await metrics.GetAsync(CancellationToken.None);
        Assert.Equal(4, cloudWatch.Requests.Count);
    }

    [Fact]
    public async Task AnS3CompatibleStore_IsUnavailable_AndCloudWatchIsNeverCalled()
    {
        FakeCloudWatch cloudWatch = new(Answer);
        OpsStorageMetrics metrics = Metrics(CompatibleStore, cloudWatch, Distribution);

        StorageMetricsSnapshot snapshot = await metrics.GetAsync(CancellationToken.None);

        Assert.False(metrics.WatchesBucket);
        Assert.Equal(OpsMetricStatuses.Unavailable, snapshot.Bucket.Status);
        Assert.Contains("S3-compatible", snapshot.Bucket.Error);
        Assert.Equal(OpsMetricStatuses.Unavailable, snapshot.Cdn.Status);
        Assert.Empty(cloudWatch.Requests);
    }

    [Fact]
    public async Task WithoutADistribution_OnlyTheBucketIsRead()
    {
        FakeCloudWatch cloudWatch = new(Answer);

        StorageMetricsSnapshot snapshot = await Metrics(AwsStore, cloudWatch, "  ").GetAsync(CancellationToken.None);

        Assert.Equal(OpsMetricStatuses.Ok, snapshot.Bucket.Status);
        Assert.Equal(OpsMetricStatuses.NotConfigured, snapshot.Cdn.Status);
        Assert.Null(snapshot.Cdn.DistributionId);
        Assert.Equal("eu-north-1", Assert.Single(cloudWatch.Requests).Region);
    }

    [Fact]
    public async Task WithoutAStore_NothingIsRead()
    {
        FakeCloudWatch cloudWatch = new(Answer);

        StorageMetricsSnapshot snapshot = await Metrics(null, cloudWatch, null).GetAsync(CancellationToken.None);

        Assert.Equal(OpsMetricStatuses.NotConfigured, snapshot.Bucket.Status);
        Assert.Equal(OpsMetricStatuses.NotConfigured, snapshot.Cdn.Status);
        Assert.Empty(cloudWatch.Requests);
    }

    [Fact]
    public void TheGap_IsTheBucketLessTheDatabaseOnTheSameDay_AndHowMuchItGrewOverTheWeek()
    {
        List<OpsStorageClassSeries> sizes =
        [
            new("StandardStorage", [Point(1, 1_000), Point(5, 1_100), Point(8, 1_300), Point(9, 1_500)]),
            new("StandardIAStorage", [Point(8, 100), Point(9, 100)])
        ];
        Dictionary<DateOnly, long> recorded = new()
        {
            [new DateOnly(2026, 11, 1)] = 900,
            [new DateOnly(2026, 11, 5)] = 1_000,
            [new DateOnly(2026, 11, 8)] = 1_100,
            [new DateOnly(2026, 11, 9)] = 1_150,
            // A day CloudWatch hasn't reported yet stays out.
            [new DateOnly(2026, 11, 10)] = 1_160
        };

        OpsStorageGap gap = OpsStorageMetrics.Gap(sizes, databaseBytes: 1_170, recorded)!;

        Assert.Equal(1_170, gap.DatabaseBytes);
        Assert.Equal(1_600, gap.BucketBytes);
        Assert.Equal(new DateOnly(2026, 11, 9), gap.BucketDay);
        Assert.Equal(450, gap.DifferenceBytes);
        Assert.Equal(["2026-11-01:100", "2026-11-05:100", "2026-11-08:300", "2026-11-09:450"],
            gap.Days.Select(d => $"{d.Day:yyyy-MM-dd}:{d.DifferenceBytes}"));
        // From the 5th (the earliest within the week before the 9th): 100 to 450.
        Assert.Equal(350, gap.GrowthLastWeekBytes);
    }

    [Fact]
    public void WithoutARecordedDay_TheGapUsesTheDatabaseNow_AndHasNoGrowth()
    {
        OpsStorageGap gap = OpsStorageMetrics.Gap([new("StandardStorage", [Point(9, 1_500)])], 1_000,
            new Dictionary<DateOnly, long>())!;

        Assert.Equal(500, gap.DifferenceBytes);
        Assert.Empty(gap.Days);
        Assert.Null(gap.GrowthLastWeekBytes);
        Assert.Null(OpsStorageMetrics.Gap([], 1_000, new Dictionary<DateOnly, long>()));
    }

    private static OpsMetricPoint Point(int day, double value) => new(Day(day), value);

    private static DateTimeOffset Day(int day) => new(2026, 11, day, 0, 0, 0, TimeSpan.Zero);

    private static DateTime Hour(int hoursAgo) =>
        new DateTime(2026, 11, 12, 15, 0, 0, DateTimeKind.Utc).AddHours(-hoursAgo);

    private static MetricDataResult Result(string id, string label, params (DateTimeOffset At, double Value)[] points) =>
        new()
        {
            Id = id,
            Label = label,
            StatusCode = StatusCode.Complete,
            Timestamps = points.Select(p => p.At.UtcDateTime).ToList(),
            Values = points.Select(p => p.Value).ToList()
        };

    private static MetricDataResult HourResult(string id, params (int HoursAgo, double Value)[] points) =>
        Result(id, id, points.Select(p => ((DateTimeOffset)Hour(p.HoursAgo), p.Value)).ToArray());

    /// <summary>A bucket with two storage classes (sent newest first) and a distribution with two busy hours.</summary>
    private static GetMetricDataResponse Answer(string region, GetMetricDataRequest request) => region switch
    {
        "eu-north-1" => new GetMetricDataResponse
        {
            MetricDataResults =
            [
                Result("sizes", "StandardIAStorage", (Day(10), 10), (Day(11), 20)),
                Result("sizes", "StandardStorage", (Day(11), 1_200), (Day(10), 1_000)),
                Result("objects", "", (Day(10), 40), (Day(11), 42))
            ]
        },
        _ => new GetMetricDataResponse
        {
            MetricDataResults =
            [
                HourResult("requests", (3, 100), (2, 900)),
                HourResult("bytes", (3, 5_000), (2, 45_000)),
                HourResult("client_errors", (3, 1), (2, 2)),
                HourResult("server_errors", (3, 10), (2, 0))
            ]
        }
    };

    private static OpsStorageMetrics Metrics(R2Options? store, FakeCloudWatch client, string? distribution,
        FakeTime? time = null)
    {
        time ??= new FakeTime(Now);
        return new OpsStorageMetrics(store, client, distribution, new OpsSnapshotCache(time), time,
            NullLogger<OpsStorageMetrics>.Instance);
    }

    private sealed class FakeCloudWatch(Func<string, GetMetricDataRequest, GetMetricDataResponse> answer)
        : ICloudWatchMetricsClient
    {
        public List<(string Region, GetMetricDataRequest Request)> Requests { get; } = [];
        public List<string?> Tokens { get; } = [];

        public Task<GetMetricDataResponse> GetMetricDataAsync(string region, GetMetricDataRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add((region, request));
            Tokens.Add(request.NextToken);
            return Task.FromResult(answer(region, request));
        }
    }

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
