using System.Net;
using System.Web;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Reelshelf.Video.R2;
using Xunit;

namespace Reelshelf.Test.Video;

/// <summary>
/// The object store's settings (<c>Storage:*</c>, or the older <c>R2:*</c>), the client each gives, and the URLs it
/// presigns. Presigning signs with the key alone, so none of this touches the network.
/// </summary>
public class StorageOptionsTests
{
    private static readonly Guid ClipId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static readonly (string, string)[] AwsStorage =
    [
        ("Storage:Region", "eu-north-1"), ("Storage:AccessKeyId", "test-access-key"),
        ("Storage:SecretAccessKey", "test-secret-key"), ("Storage:Bucket", "reelshelf-dev"),
        ("Storage:MediaBaseUrl", "https://media-dev.example/")
    ];

    private static readonly (string, string)[] LegacyR2 =
    [
        ("R2:AccountId", "acct"), ("R2:AccessKeyId", "id"), ("R2:SecretAccessKey", "secret"),
        ("R2:Bucket", "bucket"), ("R2:MediaBaseUrl", "https://media.example/")
    ];

    [Fact]
    public void Storage_WithARegionAlone_IsAwsS3()
    {
        R2Options options = Assert.IsType<R2Options>(R2Options.FromConfiguration(Configuration(AwsStorage)));

        Assert.Equal("eu-north-1", options.Region);
        Assert.Equal("", options.Endpoint);
        Assert.Equal("", options.AccountId);
        Assert.Null(options.ServiceUrl);
        Assert.Equal("test-access-key", options.AccessKeyId);
        Assert.Equal("test-secret-key", options.SecretAccessKey);
        Assert.Equal("reelshelf-dev", options.Bucket);
        Assert.Equal("https://media-dev.example", options.MediaBaseUrl);
    }

    [Fact]
    public void Storage_WithAnEndpoint_NeedsNoRegion_AndSignsForAuto()
    {
        R2Options options = Assert.IsType<R2Options>(R2Options.FromConfiguration(Configuration(
            [.. AwsStorage.Where(pair => pair.Item1 != "Storage:Region"),
                ("Storage:Endpoint", "https://acct.r2.cloudflarestorage.com/")])));

        Assert.Equal("https://acct.r2.cloudflarestorage.com", options.ServiceUrl);
        Assert.Equal("auto", options.SigningRegion);
    }

    [Fact]
    public void Storage_WithAnEndpointAndARegion_SignsForThatRegion()
    {
        R2Options options = Assert.IsType<R2Options>(R2Options.FromConfiguration(Configuration(
            [.. AwsStorage, ("Storage:Endpoint", "https://objects.example")])));

        Assert.Equal("https://objects.example", options.ServiceUrl);
        Assert.Equal("eu-north-1", options.SigningRegion);
    }

    [Fact]
    public void LegacyR2_GivesTodaysValues()
    {
        R2Options options = Assert.IsType<R2Options>(R2Options.FromConfiguration(Configuration(LegacyR2)));

        Assert.Equal("acct", options.AccountId);
        Assert.Equal("", options.Endpoint);
        Assert.Equal("", options.Region);
        Assert.Equal("https://acct.r2.cloudflarestorage.com", options.ServiceUrl);
        Assert.Equal("auto", options.SigningRegion);
        Assert.Equal("bucket", options.Bucket);
        Assert.Equal("https://media.example", options.MediaBaseUrl);
    }

    [Fact]
    public void TheStorageSectionsOtherSettings_AreNotTheObjectStore()
    {
        IConfiguration configuration = Configuration(
            [.. LegacyR2, ("Storage:BackstopBytes", "1099511627776"), ("Storage:WarnAtPercent", "90")]);

        Assert.Equal("acct", R2Options.FromConfiguration(configuration)?.AccountId);
        Assert.Null(R2Options.FromConfiguration(Configuration(("Storage:WarnAtPercent", "90"))));
    }

    [Fact]
    public void BothSections_FailStartup()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            R2Options.FromConfiguration(Configuration([.. AwsStorage, ("R2:AccountId", "acct")])));

        Assert.Contains("configured twice", error.Message);
        Assert.Contains("Storage:Bucket", error.Message);
        Assert.Contains("R2:AccountId", error.Message);
        Assert.DoesNotContain("test-secret-key", error.Message);
    }

    [Fact]
    public void APartialStorageSection_NamesWhatIsMissing()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            R2Options.FromConfiguration(Configuration(("Storage:Bucket", "bucket"), ("Storage:AccessKeyId", "id"))));

        Assert.Equal(
            "Storage is partly configured: Storage:Region, Storage:SecretAccessKey, Storage:MediaBaseUrl are missing",
            error.Message);
    }

    [Fact]
    public void AnEndpointAlone_IsAPartialSection()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            R2Options.FromConfiguration(Configuration(("Storage:Endpoint", "https://objects.example"))));

        Assert.DoesNotContain("Storage:Region", error.Message);
        Assert.Contains("Storage:AccessKeyId", error.Message);
    }

    [Theory]
    [InlineData("Storage:Endpoint", "http://objects.example")]
    [InlineData("Storage:Endpoint", "https://objects.example/bucket")]
    [InlineData("Storage:Endpoint", "objects.example")]
    [InlineData("Storage:MediaBaseUrl", "https://pub-123.r2.dev")]
    [InlineData("Storage:MediaBaseUrl", "https://media.example/clips")]
    [InlineData("Storage:Region", "auto")]
    [InlineData("Storage:Region", "EU-NORTH-1")]
    [InlineData("Storage:Region", "https://s3.eu-north-1.amazonaws.com")]
    public void Storage_RefusesABadValue(string key, string value)
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => R2Options.FromConfiguration(
            Configuration([.. AwsStorage.Where(pair => pair.Item1 != key), (key, value)])));

        Assert.StartsWith(key, error.Message);
    }

    [Fact]
    public void AnotherBucket_KeepsTheStoreAndCredentials()
    {
        R2Options media = R2Options.FromConfiguration(Configuration(AwsStorage))!;

        R2Options evidence = media.WithBucket("reelshelf-evidence");

        Assert.Equal("reelshelf-evidence", evidence.Bucket);
        Assert.Equal(media.Region, evidence.Region);
        Assert.Equal(media.AccessKeyId, evidence.AccessKeyId);
        Assert.Equal(media.SecretAccessKey, evidence.SecretAccessKey);
        Assert.Equal(media.ServiceUrl, evidence.ServiceUrl);
        Assert.Equal("https://acct.r2.cloudflarestorage.com",
            R2Options.FromConfiguration(Configuration(LegacyR2))!.WithBucket("evidence").ServiceUrl);
    }

    [Fact]
    public void AwsS3_IsReachedFromTheRegion_VirtualHosted()
    {
        AmazonS3Config config = R2Storage.ClientConfig(R2Options.FromConfiguration(Configuration(AwsStorage))!);

        Assert.Equal(RegionEndpoint.EUNorth1, config.RegionEndpoint);
        Assert.Null(config.ServiceURL);
        Assert.Null(config.AuthenticationRegion);
        Assert.False(config.ForcePathStyle);
        Assert.True(config.IgnoreConfiguredEndpointUrls);
        Assert.Equal(RequestChecksumCalculation.WHEN_REQUIRED, config.RequestChecksumCalculation);
        Assert.Equal(ResponseChecksumValidation.WHEN_REQUIRED, config.ResponseChecksumValidation);
    }

    [Fact]
    public void AnEndpoint_IsCalledPathStyle_AndSignedForItsRegion()
    {
        AmazonS3Config config = R2Storage.ClientConfig(R2Options.FromConfiguration(Configuration(LegacyR2))!);

        Assert.Equal("https://acct.r2.cloudflarestorage.com/", config.ServiceURL);
        Assert.Equal("auto", config.AuthenticationRegion);
        Assert.True(config.ForcePathStyle);
        Assert.Equal(RequestChecksumCalculation.WHEN_REQUIRED, config.RequestChecksumCalculation);
        Assert.Equal(ResponseChecksumValidation.WHEN_REQUIRED, config.ResponseChecksumValidation);
    }

    [Fact]
    public void OnAwsS3_APresignedPut_IsVirtualHosted_AndSignsTheContentTypeAndCacheControl()
    {
        using R2Storage storage = AwsStorageClient();

        Uri url = new(storage.PresignPut($"clips/{ClipId}/v1/av1.mp4", "video/mp4", R2ObjectKeys.ImmutableCacheControl,
            TimeSpan.FromHours(2)));

        Assert.Equal("https", url.Scheme);
        Assert.Equal("reelshelf-dev.s3.eu-north-1.amazonaws.com", url.Host);
        Assert.Equal($"/clips/{ClipId}/v1/av1.mp4", url.AbsolutePath);
        var query = HttpUtility.ParseQueryString(url.Query);
        Assert.Equal("AWS4-HMAC-SHA256", query["X-Amz-Algorithm"]);
        Assert.StartsWith("test-access-key/", query["X-Amz-Credential"]);
        Assert.EndsWith("/eu-north-1/s3/aws4_request", query["X-Amz-Credential"]);
        Assert.Equal("7200", query["X-Amz-Expires"]);
        Assert.Equal(["cache-control", "content-type", "host"], Sorted(query["X-Amz-SignedHeaders"]!.Split(';')));
        Assert.DoesNotContain("test-secret-key", url.ToString());
    }

    [Fact]
    public void OnAwsS3_APresignedGet_CarriesTheDisposition_AndNeverOutlivesSevenDays()
    {
        using R2Storage storage = AwsStorageClient();

        Uri url = new(storage.PresignGet($"clips/{ClipId}/v1/av1.mp4", TimeSpan.FromDays(30),
            "attachment; filename=\"Clip.mp4\""));

        Assert.Equal("reelshelf-dev.s3.eu-north-1.amazonaws.com", url.Host);
        var query = HttpUtility.ParseQueryString(url.Query);
        Assert.Equal("attachment; filename=\"Clip.mp4\"", query["response-content-disposition"]);
        Assert.EndsWith("/eu-north-1/s3/aws4_request", query["X-Amz-Credential"]);
        Assert.Equal("host", query["X-Amz-SignedHeaders"]);
        Assert.Equal("604800", query["X-Amz-Expires"]);
    }

    [Fact]
    public void OnAwsS3_APresignedPart_SignsOnlyTheHost()
    {
        using R2Storage storage = AwsStorageClient();

        Uri url = new(storage.PresignUploadPart(R2ObjectKeys.Upload(ClipId), "upload-1", 7, TimeSpan.FromHours(1)));

        Assert.Equal("reelshelf-dev.s3.eu-north-1.amazonaws.com", url.Host);
        Assert.Equal($"/uploads/{ClipId}", url.AbsolutePath);
        var query = HttpUtility.ParseQueryString(url.Query);
        Assert.Equal("upload-1", query["uploadId"]);
        Assert.Equal("7", query["partNumber"]);
        Assert.Equal("host", query["X-Amz-SignedHeaders"]);
        Assert.EndsWith("/eu-north-1/s3/aws4_request", query["X-Amz-Credential"]);
        Assert.Null(query["x-amz-sdk-checksum-algorithm"]);
    }

    [Fact]
    public void AtAnEndpoint_PresignedUrls_ArePathStyle_AndSignedForItsRegion()
    {
        R2Options options = R2Options.FromConfiguration(Configuration(
            [.. AwsStorage, ("Storage:Endpoint", "https://objects.example")]))!;
        using R2Storage storage = new(options, NullLogger<R2Storage>.Instance);

        Uri part = new(storage.PresignUploadPart(R2ObjectKeys.Upload(ClipId), "upload-1", 1, TimeSpan.FromHours(1)));
        Uri put = new(storage.PresignPut($"clips/{ClipId}/v1/thumbnail.jpg", "image/jpeg",
            R2ObjectKeys.ImmutableCacheControl, TimeSpan.FromHours(1)));

        Assert.Equal("objects.example", part.Host);
        Assert.Equal($"/reelshelf-dev/uploads/{ClipId}", part.AbsolutePath);
        Assert.EndsWith("/eu-north-1/s3/aws4_request", HttpUtility.ParseQueryString(part.Query)["X-Amz-Credential"]);
        Assert.Equal($"/reelshelf-dev/clips/{ClipId}/v1/thumbnail.jpg", put.AbsolutePath);
        Assert.Equal(["cache-control", "content-type", "host"],
            Sorted(HttpUtility.ParseQueryString(put.Query)["X-Amz-SignedHeaders"]!.Split(';')));
    }

    [Fact]
    public async Task OnAwsS3_UploadsSendThePlainUnsignedBody_AndStartMultipartUploadsWithoutAChecksum()
    {
        // A multipart upload started with a checksum algorithm needs every part's checksum at completion, which the
        // browser's presigned part PUTs never send; and aws-chunked trailers would be a second wire format to rely on.
        R2Options options = R2Options.FromConfiguration(Configuration(AwsStorage))!;
        RecordingHandler handler = new();
        AmazonS3Config config = R2Storage.ClientConfig(options);
        config.HttpClientFactory = new RecordingHttpClientFactory(handler);
        using R2Storage storage = new(options,
            new AmazonS3Client(R2Storage.Credentials(options), config), NullLogger<R2Storage>.Instance);

        await storage.StartMultipartUploadAsync(R2ObjectKeys.Upload(ClipId), "video/mp4", CancellationToken.None);
        await storage.PutAsync("game-art/x/cover/big/1.jpg", [1, 2, 3], "image/jpeg", R2ObjectKeys.ImmutableCacheControl,
            CancellationToken.None);
        using MemoryStream source = new(new byte[R2Storage.PartSize + 10]);
        await storage.UploadAsync(R2ObjectKeys.Upload(ClipId), source, "video/mp4", CancellationToken.None);

        Assert.All(handler.Requests, request =>
        {
            Assert.StartsWith("reelshelf-dev.s3.eu-north-1.amazonaws.com", request.RequestUri!.Authority);
            Assert.False(request.Headers.Contains("x-amz-sdk-checksum-algorithm"));
            Assert.False(request.Headers.Contains("x-amz-checksum-algorithm"));
            Assert.False(request.Headers.Contains("X-Amz-Trailer"));
            Assert.Contains("/eu-north-1/s3/aws4_request", string.Join(",", request.Headers.GetValues("Authorization")));
        });
        List<HttpRequestMessage> bodies = handler.Requests.Where(request => request.Method == HttpMethod.Put).ToList();
        Assert.Equal(3, bodies.Count);
        Assert.All(bodies, request =>
            Assert.Equal("UNSIGNED-PAYLOAD", request.Headers.GetValues("X-Amz-Content-SHA256").Single()));
        Assert.Equal(R2ObjectKeys.ImmutableCacheControl, bodies[0].Headers.CacheControl?.ToString());
    }

    private static R2Storage AwsStorageClient() =>
        new(R2Options.FromConfiguration(Configuration(AwsStorage))!, NullLogger<R2Storage>.Instance);

    private static string[] Sorted(IEnumerable<string> values) => values.Order(StringComparer.Ordinal).ToArray();

    private static IConfiguration Configuration(params (string Key, string Value)[] values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(value => new KeyValuePair<string, string?>(value.Key, value.Value)))
            .Build();
    }

    /// <summary>Answers every request as S3 would a success, and keeps them.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                await request.Content.LoadIntoBufferAsync(cancellationToken);
            }

            Requests.Add(request);
            string query = request.RequestUri!.Query;
            string body = request.Method == HttpMethod.Post && query.Contains("uploads")
                ? "<InitiateMultipartUploadResult><Bucket>reelshelf-dev</Bucket><Key>k</Key><UploadId>upload-1</UploadId></InitiateMultipartUploadResult>"
                : request.Method == HttpMethod.Post
                    ? "<CompleteMultipartUploadResult><ETag>\"whole\"</ETag></CompleteMultipartUploadResult>"
                    : "";
            HttpResponseMessage response = new(HttpStatusCode.OK) { Content = new StringContent(body) };
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"part\"");
            return response;
        }
    }

    private sealed class RecordingHttpClientFactory(RecordingHandler handler) : HttpClientFactory
    {
        public override HttpClient CreateHttpClient(IClientConfig clientConfig) => new(handler, disposeHandler: false);

        public override bool UseSDKHttpClientCaching(IClientConfig clientConfig) => false;

        public override bool DisposeHttpClientsAfterUse(IClientConfig clientConfig) => false;
    }
}
