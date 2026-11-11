using System.Net;
using System.Xml.Linq;
using Amazon.CloudFront;
using Amazon.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Reelshelf.Moderation;
using Xunit;

namespace Reelshelf.Test.Moderation;

/// <summary>
/// The CloudFront purge: which paths a removed clip is invalidated by, the request the SDK puts on the wire (answered
/// by a handler, so nothing touches the network), and how <c>Moderation:*</c> picks a purger.
/// </summary>
public class CloudFrontCdnPurgerTests
{
    private const string DistributionId = "E2EXAMPLE123";
    private static readonly Guid ClipId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly XNamespace CloudFront = "http://cloudfront.amazonaws.com/doc/2020-05-31/";

    [Fact]
    public void AClipIsInvalidatedByItsWholeFolderAndItsUpload()
    {
        Assert.Equal(
            ["/clips/11111111-2222-3333-4444-555555555555/*", "/uploads/11111111-2222-3333-4444-555555555555*"],
            CloudFrontCdnPurger.ClipPaths(ClipId));
    }

    [Fact]
    public async Task AnAcceptedInvalidationIsPurged_AndSignedForCloudFrontsGlobalEndpoint()
    {
        RecordingHandler handler = new(Created);
        using CloudFrontCdnPurger purger = Purger(handler);

        string result = await purger.PurgeClipAsync(ClipId, maxVersion: 3, CancellationToken.None);

        Assert.Equal(CdnPurgeResults.Purged, result);
        (HttpRequestMessage request, string body) = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("cloudfront.amazonaws.com", request.RequestUri!.Host);
        Assert.Equal($"/2020-05-31/distribution/{DistributionId}/invalidation", request.RequestUri.AbsolutePath);
        string authorization = string.Join(",", request.Headers.GetValues("Authorization"));
        Assert.Contains("Credential=test-access-key/", authorization);
        Assert.Contains("/us-east-1/cloudfront/aws4_request", authorization);
        Assert.DoesNotContain("test-secret-key", authorization);

        XElement batch = XDocument.Parse(body).Root!;
        Assert.Equal(CloudFront + "InvalidationBatch", batch.Name);
        Assert.Equal("2", batch.Element(CloudFront + "Paths")!.Element(CloudFront + "Quantity")!.Value);
        Assert.Equal(CloudFrontCdnPurger.ClipPaths(ClipId),
            batch.Descendants(CloudFront + "Path").Select(path => path.Value).ToList());
        Assert.StartsWith($"{ClipId:N}-", batch.Element(CloudFront + "CallerReference")!.Value);
    }

    [Fact]
    public async Task EachPurgeIsANewInvalidation_SoTheLaterSweepIsNotTakenForTheFirst()
    {
        RecordingHandler handler = new(Created, Created);
        using CloudFrontCdnPurger purger = Purger(handler);

        await purger.PurgeClipAsync(ClipId, 1, CancellationToken.None);
        await purger.PurgeClipAsync(ClipId, 1, CancellationToken.None);

        List<string> references = handler.Requests.Select(request => CallerReference(request.Body)).ToList();
        Assert.Equal(2, references.Distinct().Count());
    }

    [Fact]
    public async Task TheSdksRetryOfOnePurgeResendsItsCallerReference_SoCloudFrontAnswersTheSameInvalidation()
    {
        RecordingHandler handler = new(
            () => Error(HttpStatusCode.InternalServerError, "ServiceUnavailable", "try again"), Created);
        using CloudFrontCdnPurger purger = Purger(handler);

        string result = await purger.PurgeClipAsync(ClipId, 1, CancellationToken.None);

        Assert.Equal(CdnPurgeResults.Purged, result);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(CallerReference(handler.Requests[0].Body), CallerReference(handler.Requests[1].Body));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "TooManyInvalidationsInProgress",
        "Processing your request will cause you to exceed the maximum number of in-progress wildcard invalidations.")]
    [InlineData(HttpStatusCode.Forbidden, "AccessDenied", "User is not authorized to perform cloudfront:CreateInvalidation")]
    [InlineData(HttpStatusCode.NotFound, "NoSuchDistribution", "The specified distribution does not exist.")]
    public async Task ARefusedInvalidationThrows_NamingTheClipDistributionAndCause(HttpStatusCode status, string code,
        string message)
    {
        RecordingHandler handler = new(() => Error(status, code, message));
        using CloudFrontCdnPurger purger = Purger(handler);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            purger.PurgeClipAsync(ClipId, 1, CancellationToken.None));

        Assert.Contains(ClipId.ToString(), error.Message);
        Assert.Contains(DistributionId, error.Message);
        Assert.Contains($"{(int)status} {code}", error.Message);
        Assert.Contains(message, error.Message);
        Assert.DoesNotContain("test-secret-key", error.Message);
        Assert.DoesNotContain("test-access-key", error.Message);
    }

    private static readonly (string, string)[] AwsStorage =
    [
        ("Storage:Region", "eu-north-1"), ("Storage:AccessKeyId", "test-access-key"),
        ("Storage:SecretAccessKey", "test-secret-key"), ("Storage:Bucket", "reelshelf-dev"),
        ("Storage:MediaBaseUrl", "https://media-dev.example")
    ];

    private static readonly (string, string)[] Cloudflare =
        [("Moderation:Cloudflare:ZoneId", "zone"), ("Moderation:Cloudflare:ApiToken", "token")];

    [Fact]
    public void ADistributionOnAnAwsStorePurgesThroughCloudFront()
    {
        using ServiceProvider services = Services([.. AwsStorage, ("Moderation:CloudFront:DistributionId", DistributionId)]);

        Assert.IsType<CloudFrontCdnPurger>(services.GetRequiredService<ICdnPurger>());
    }

    [Fact]
    public void AZoneStillPurgesThroughCloudflare()
    {
        using ServiceProvider services = Services([.. AwsStorage, .. Cloudflare]);

        Assert.IsType<CloudflareCdnPurger>(services.GetRequiredService<ICdnPurger>());
    }

    [Theory]
    [InlineData("Moderation:Cloudflare:ZoneId")]
    [InlineData("Moderation:Cloudflare:ApiToken")]
    public void BothCdnsFailStartup_EvenWithHalfOfCloudflare(string cloudflareKey)
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            Services([.. AwsStorage, ("Moderation:CloudFront:DistributionId", DistributionId), (cloudflareKey, "x")]));

        Assert.Contains("Moderation:CloudFront", error.Message);
        Assert.Contains("Moderation:Cloudflare", error.Message);
    }

    [Fact]
    public async Task NeitherCdnLeavesThePurgeToBeDoneByHand()
    {
        using ServiceProvider services = Services(AwsStorage);

        Assert.Equal(CdnPurgeResults.NotConfigured,
            await services.GetRequiredService<ICdnPurger>().PurgeClipAsync(ClipId, 1, CancellationToken.None));
    }

    [Fact]
    public async Task WithoutAnObjectStoreThereIsNothingToPurge()
    {
        using ServiceProvider services = Services(("Moderation:CloudFront:DistributionId", DistributionId));

        Assert.Equal(CdnPurgeResults.NotNeeded,
            await services.GetRequiredService<ICdnPurger>().PurgeClipAsync(ClipId, 1, CancellationToken.None));
    }

    [Fact]
    public void ADistributionNeedsTheStoreOnAwsS3_WhoseKeysSignTheInvalidations()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => Services(
            [.. AwsStorage, ("Storage:Endpoint", "https://objects.example"), ("Moderation:CloudFront:DistributionId", DistributionId)]));

        Assert.Contains("AWS S3", error.Message);
    }

    private static ServiceProvider Services(params (string Key, string Value)[] values)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(
            values.Select(value => new KeyValuePair<string, string?>(value.Key, value.Value)));
        builder.AddModeration();
        return builder.Services.BuildServiceProvider();
    }

    private static CloudFrontCdnPurger Purger(RecordingHandler handler)
    {
        AmazonCloudFrontConfig config = CloudFrontCdnPurger.ClientConfig();
        config.HttpClientFactory = new RecordingHttpClientFactory(handler);
        return new CloudFrontCdnPurger(
            new AmazonCloudFrontClient(new BasicAWSCredentials("test-access-key", "test-secret-key"), config),
            DistributionId);
    }

    private static string CallerReference(string body) =>
        XDocument.Parse(body).Root!.Element(CloudFront + "CallerReference")!.Value;

    private static HttpResponseMessage Created()
    {
        HttpResponseMessage response = new(HttpStatusCode.Created)
        {
            Content = new StringContent(
                $"""
                 <?xml version="1.0" encoding="UTF-8"?>
                 <Invalidation xmlns="{CloudFront}"><Id>I2J0I21PCUYOIK</Id><Status>InProgress</Status>
                 <CreateTime>2026-11-11T10:00:00Z</CreateTime><InvalidationBatch><Paths><Quantity>2</Quantity><Items>
                 <Path>/clips/{ClipId}/*</Path><Path>/uploads/{ClipId}*</Path></Items></Paths>
                 <CallerReference>ref</CallerReference></InvalidationBatch></Invalidation>
                 """, System.Text.Encoding.UTF8, "text/xml")
        };
        response.Headers.Location = new Uri(
            $"https://cloudfront.amazonaws.com/2020-05-31/distribution/{DistributionId}/invalidation/I2J0I21PCUYOIK");
        return response;
    }

    private static HttpResponseMessage Error(HttpStatusCode status, string code, string message) => new(status)
    {
        Content = new StringContent(
            $"""
             <?xml version="1.0"?>
             <ErrorResponse xmlns="{CloudFront}"><Error><Type>Sender</Type><Code>{code}</Code>
             <Message>{message}</Message></Error><RequestId>req-1</RequestId></ErrorResponse>
             """, System.Text.Encoding.UTF8, "text/xml")
    };

    /// <summary>Answers each request with the next response (the last one again once they run out), and keeps them.</summary>
    private sealed class RecordingHandler(params Func<HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request, body));
            return responses[Math.Min(Requests.Count, responses.Length) - 1]();
        }
    }

    private sealed class RecordingHttpClientFactory(RecordingHandler handler) : HttpClientFactory
    {
        public override HttpClient CreateHttpClient(IClientConfig clientConfig) => new(handler, disposeHandler: false);

        public override bool UseSDKHttpClientCaching(IClientConfig clientConfig) => false;

        public override bool DisposeHttpClientsAfterUse(IClientConfig clientConfig) => false;
    }
}
