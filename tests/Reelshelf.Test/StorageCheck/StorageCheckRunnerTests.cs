using System.Net;
using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using Reelshelf.StorageCheck;
using Reelshelf.Test.Video;
using Xunit;

namespace Reelshelf.Test.StorageCheck;

/// <summary>
/// The guarantee the preflight is run on: whatever its checks do or don't get to, nothing of the run is left in either
/// bucket and nothing that was already there is touched. The buckets are in-memory fakes and every HTTP request (the
/// presigned URLs, CORS, the CDN) is refused, so most checks fail, which is the case worth testing.
/// </summary>
public class StorageCheckRunnerTests
{
    private static readonly string[] RealMediaKeys =
    [
        "uploads/00000000-0000-4000-8000-000000000001",
        "clips/00000000-0000-4000-8000-000000000001/v1/av1.mp4",
        "quarantine/00000000-0000-4000-8000-000000000003/clips/x/v1/av1.mp4",
        "game-art/00000000-0000-4000-8000-000000000004/cover/t_cover_big/a.jpg"
    ];

    private const string RealEvidenceKey = "00000000-0000-4000-8000-000000000002/record.json";

    [Fact]
    public async Task FailingChecks_StillLeaveBothBucketsAsTheyWere()
    {
        FakeR2Client media = Seeded(new FakeR2Client(), RealMediaKeys);
        EvidenceBucketClient evidence = new();
        evidence.Objects[RealEvidenceKey] = 2;
        StringWriter output = new();

        using StorageCheckRunner runner = new(Settings(), Services(media, evidence, new RefusingHandler()), output);
        int exit = await runner.RunAsync(CancellationToken.None);

        Assert.Equal(StorageCheckRunner.ExitFailed, exit);
        Assert.Contains(runner.Report.Results, result => result.Outcome == Outcome.Fail);
        // The run did write: the server-side uploads went through the fake.
        Assert.Contains(runner.Report.Results, result => result is { Id: "server.upload-multipart", Outcome: Outcome.Pass });
        Assert.Equal(RealMediaKeys.Order(), media.Objects.Keys.Order());
        Assert.Empty(media.Uploads);
        Assert.Equal([RealEvidenceKey], evidence.Objects.Keys);
        Assert.Empty(evidence.Uploads);
        Assert.Contains("Cleanup: verified", output.ToString());
        // The API's key may not list open uploads in the evidence bucket; cleanup says so rather than failing.
        Assert.Contains("may not list open multipart uploads", output.ToString());
    }

    [Fact]
    public async Task AnInterruptedRun_StillCleansUp()
    {
        FakeR2Client media = Seeded(new FakeR2Client(), RealMediaKeys);
        EvidenceBucketClient evidence = new();
        using CancellationTokenSource interrupt = new();
        // Cancelled at the browser's first part: its multipart upload is open by then.
        RefusingHandler handler = new(onRequest: request =>
        {
            if (request.Method == HttpMethod.Put)
            {
                interrupt.Cancel();
            }
        });

        using StorageCheckRunner runner = new(Settings(), Services(media, evidence, handler), new StringWriter());
        int exit = await runner.RunAsync(interrupt.Token);

        Assert.Equal(StorageCheckRunner.ExitInterrupted, exit);
        Assert.NotEmpty(media.Aborted);
        Assert.Equal(RealMediaKeys.Order(), media.Objects.Keys.Order());
        Assert.Empty(media.Uploads);
        Assert.Empty(evidence.Objects);
    }

    [Fact]
    public async Task CleanupThatCantDelete_SaysWhatIsLeft_AndExitsNonZero()
    {
        StuckBucketClient media = new();
        StringWriter output = new();

        using StorageCheckRunner runner = new(Settings(evidence: false),
            Services(media, null, new RefusingHandler()), output);
        int exit = await runner.RunAsync(CancellationToken.None);

        Assert.Equal(StorageCheckRunner.ExitCleanupIncomplete, exit);
        string text = output.ToString();
        Assert.Contains("CLEANUP INCOMPLETE", text);
        Assert.Contains($"left: media: uploads/{runner.Run.ServerClipId}", text);
    }

    [Fact]
    public void TheReport_NeverPrintsTheKeyOrASignature()
    {
        StringWriter output = new();
        CheckReport report = new(output, ["very-secret-key", "AKIAEXAMPLE"]);

        report.Add("x", "y", Outcome.Fail,
            "https://bucket/key?X-Amz-Credential=AKIAEXAMPLE%2F20261112&X-Amz-Signature=abcdef0123 refused very-secret-key");

        string text = output.ToString();
        Assert.DoesNotContain("very-secret-key", text);
        Assert.DoesNotContain("AKIAEXAMPLE", text);
        Assert.DoesNotContain("abcdef0123", text);
    }

    [Fact]
    public void TheRunsKeys_AreItsOwn()
    {
        RunNamespace run = new();

        Assert.True(run.OwnsMediaKey($"uploads/{run.ClipId}"));
        Assert.True(run.OwnsMediaKey($"clips/{run.ServerClipId}/v1/av1.mp4"));
        Assert.True(run.OwnsMediaKey($"quarantine/{run.EvidenceId}/uploads/{run.ServerClipId}"));
        Assert.True(run.OwnsEvidenceKey($"{run.EvidenceId}/record.json"));
        Assert.All(RealMediaKeys, key => Assert.False(run.OwnsMediaKey(key)));
        Assert.False(run.OwnsMediaKey($"uploads/{run.ClipId}x"));
        Assert.False(run.OwnsEvidenceKey(RealEvidenceKey));
    }

    private static FakeR2Client Seeded(FakeR2Client client, IEnumerable<string> keys)
    {
        foreach (string key in keys)
        {
            client.Objects[key] = 9;
        }

        return client;
    }

    private static CheckSettings Settings(bool evidence = true) =>
        CheckSettings.Parse(["--site-origin", "https://reelshelf.app", "--no-purge-wait"],
            new Dictionary<string, string>
            {
                ["R2__AccountId"] = FakeR2Client.Options.AccountId,
                ["R2__AccessKeyId"] = "ignored",
                ["Storage__Endpoint"] = "https://storage.example",
                ["Storage__Bucket"] = FakeR2Client.Options.Bucket,
                ["Storage__AccessKeyId"] = FakeR2Client.Options.AccessKeyId,
                ["Storage__SecretAccessKey"] = FakeR2Client.Options.SecretAccessKey,
                ["Storage__MediaBaseUrl"] = FakeR2Client.Options.MediaBaseUrl,
                ["Moderation__EvidenceBucket"] = evidence ? "evidence" : ""
            }, _ => [])!;

    private static CheckServices Services(FakeR2Client media, FakeR2Client? evidence, HttpMessageHandler handler) => new()
    {
        S3Client = options => options.Bucket == "evidence" && evidence is not null ? evidence : media,
        StorageHandler = handler,
        MediaHandler = handler,
        Delay = (_, _) => Task.CompletedTask
    };

    /// <summary>Refuses every request with S3's 403, so nothing over HTTP succeeds and nothing retries.</summary>
    private sealed class RefusingHandler(Action<HttpRequestMessage>? onRequest = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            onRequest?.Invoke(request);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("<Error><Code>AccessDenied</Code></Error>", Encoding.UTF8, "application/xml")
            });
        }
    }

    /// <summary>The evidence bucket as the API's key sees it: it may not list open multipart uploads.</summary>
    private sealed class EvidenceBucketClient : FakeR2Client
    {
        public override Task<ListMultipartUploadsResponse> ListMultipartUploadsAsync(ListMultipartUploadsRequest request,
            CancellationToken cancellationToken = default) =>
            throw new AmazonS3Exception("Access Denied") { StatusCode = HttpStatusCode.Forbidden, ErrorCode = "AccessDenied" };
    }

    /// <summary>A bucket that takes writes but refuses every delete.</summary>
    private sealed class StuckBucketClient : FakeR2Client
    {
        public override Task<DeleteObjectResponse> DeleteObjectAsync(string bucketName, string key,
            CancellationToken cancellationToken = default) =>
            throw new AmazonS3Exception("Access Denied") { StatusCode = HttpStatusCode.Forbidden, ErrorCode = "AccessDenied" };
    }
}
