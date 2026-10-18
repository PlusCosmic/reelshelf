using System.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Reelshelf.Video.R2;
using Reelshelf.Video.Transcoding;
using Xunit;

namespace Reelshelf.Test.Video;

/// <summary>
/// The R2 object layout and presigning. Presigning signs with the key alone, so none of this touches the network.
/// </summary>
public class R2StorageTests
{
    private static readonly Guid ClipId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static readonly R2Options Options = new()
    {
        AccountId = "0123456789abcdef",
        AccessKeyId = "test-access-key",
        SecretAccessKey = "test-secret-key",
        Bucket = "reelshelf-dev",
        MediaBaseUrl = "https://media-dev.example"
    };

    private static readonly string[] Thumbnails =
        ["thumbnail.jpg", "thumbnail_1.jpg", "thumbnail_2.jpg", "thumbnail_3.jpg", "thumbnail_4.jpg", "thumbnail_5.jpg"];

    [Fact]
    public void Keys_FollowTheLayout()
    {
        Assert.Equal($"uploads/{ClipId}", R2ObjectKeys.Upload(ClipId));
        Assert.Equal($"clips/{ClipId}/", R2ObjectKeys.ClipPrefix(ClipId));
        Assert.Equal($"clips/{ClipId}/v3", R2ObjectKeys.VersionFolder(ClipId, 3));
        Assert.Equal($"clips/{ClipId}/v1/av1.mp4", R2ObjectKeys.Output(ClipId, 1, R2ObjectKeys.Av1));
        Assert.Throws<ArgumentOutOfRangeException>(() => R2ObjectKeys.VersionFolder(ClipId, 0));
    }

    [Fact]
    public void EachJobType_WritesItsOwnFiles()
    {
        Assert.Equal(["av1.mp4", .. Thumbnails, "preview.mp4"], R2ObjectKeys.OutputsFor(TranscodeRunTypes.Transcode));
        Assert.Equal(Thumbnails, R2ObjectKeys.OutputsFor(TranscodeRunTypes.Thumbnails));
        Assert.Equal(["av1.mp4", .. Thumbnails], R2ObjectKeys.OutputsFor(TranscodeRunTypes.Trim));
        Assert.Empty(R2ObjectKeys.OutputsFor(TranscodeRunTypes.Fallback));
        Assert.Equal("video/mp4", R2ObjectKeys.ContentType("preview.mp4"));
        Assert.Equal("image/jpeg", R2ObjectKeys.ContentType("thumbnail_3.jpg"));
    }

    [Fact]
    public void PresignedPut_SignsTheContentTypeAndCacheControl()
    {
        using R2Storage storage = new(Options, NullLogger<R2Storage>.Instance);

        Uri url = new(storage.PresignPut($"clips/{ClipId}/v1/av1.mp4", "video/mp4", R2ObjectKeys.ImmutableCacheControl,
            TimeSpan.FromHours(2)));

        Assert.Equal("https", url.Scheme);
        Assert.Equal("0123456789abcdef.r2.cloudflarestorage.com", url.Host);
        Assert.Equal($"/reelshelf-dev/clips/{ClipId}/v1/av1.mp4", url.AbsolutePath);
        var query = HttpUtility.ParseQueryString(url.Query);
        Assert.Equal("7200", query["X-Amz-Expires"]);
        Assert.EndsWith("/auto/s3/aws4_request", query["X-Amz-Credential"]);
        Assert.StartsWith("test-access-key/", query["X-Amz-Credential"]);
        Assert.Equal(["cache-control", "content-type", "host"], Sorted(query["X-Amz-SignedHeaders"]!.Split(';')));
        Assert.False(string.IsNullOrEmpty(query["X-Amz-Signature"]));
        Assert.DoesNotContain("test-secret-key", url.ToString());
    }

    [Fact]
    public void PresignedUrls_NeverOutliveSevenDays()
    {
        using R2Storage storage = new(Options, NullLogger<R2Storage>.Instance);

        Uri url = new(storage.PresignGet(R2ObjectKeys.Upload(ClipId), TimeSpan.FromDays(30)));

        Assert.Equal("604800", HttpUtility.ParseQueryString(url.Query)["X-Amz-Expires"]);
    }

    [Fact]
    public void ATranscode_ReadsTheUpload_AndWritesEveryOutputOfItsVersion()
    {
        using R2Storage storage = new(Options, NullLogger<R2Storage>.Instance);

        TranscodeJobUrls urls = storage.UrlsFor(ClipId, TranscodeRunTypes.Transcode, 1, 1, TimeSpan.FromHours(2));

        Assert.Equal($"/reelshelf-dev/uploads/{ClipId}", new Uri(urls.SourceUrl!).AbsolutePath);
        IReadOnlyDictionary<string, string> uploads = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(urls.UploadUrls);
        Assert.Equal(Sorted(["av1.mp4", .. Thumbnails, "preview.mp4"]), Sorted(uploads.Keys));
        foreach ((string fileName, string url) in uploads)
        {
            Assert.Equal($"/reelshelf-dev/clips/{ClipId}/v1/{fileName}", new Uri(url).AbsolutePath);
        }
    }

    [Fact]
    public void ATrim_ReadsTheCurrentVersion_AndWritesTheNext()
    {
        using R2Storage storage = new(Options, NullLogger<R2Storage>.Instance);

        TranscodeJobUrls urls = storage.UrlsFor(ClipId, TranscodeRunTypes.Trim, 3, 2, TimeSpan.FromHours(2));

        Assert.Equal($"/reelshelf-dev/clips/{ClipId}/v2/av1.mp4", new Uri(urls.SourceUrl!).AbsolutePath);
        Assert.Equal(Sorted(["av1.mp4", .. Thumbnails]), Sorted(urls.UploadUrls!.Keys));
        Assert.Equal($"/reelshelf-dev/clips/{ClipId}/v3/av1.mp4", new Uri(urls.UploadUrls["av1.mp4"]).AbsolutePath);
    }

    [Fact]
    public void AThumbnailsJob_ReadsAndRewritesItsVersion()
    {
        using R2Storage storage = new(Options, NullLogger<R2Storage>.Instance);

        TranscodeJobUrls urls = storage.UrlsFor(ClipId, TranscodeRunTypes.Thumbnails, 2, 2, TimeSpan.FromHours(2));

        Assert.Equal($"/reelshelf-dev/clips/{ClipId}/v2/av1.mp4", new Uri(urls.SourceUrl!).AbsolutePath);
        Assert.Equal(Sorted(Thumbnails), Sorted(urls.UploadUrls!.Keys));
    }

    [Fact]
    public void Options_AreOffWhenNothingIsSet()
    {
        Assert.Null(R2Options.FromConfiguration(Configuration()));
    }

    [Fact]
    public void Options_ReadEveryKey_AndDropTheMediaUrlsTrailingSlash()
    {
        R2Options? options = R2Options.FromConfiguration(Configuration(
            ("R2:AccountId", "acct"), ("R2:AccessKeyId", "id"), ("R2:SecretAccessKey", "secret"),
            ("R2:Bucket", "bucket"), ("R2:MediaBaseUrl", "https://media.example/")));

        Assert.NotNull(options);
        Assert.Equal("https://media.example", options.MediaBaseUrl);
        Assert.Equal("https://acct.r2.cloudflarestorage.com", options.ServiceUrl);
    }

    [Theory]
    [InlineData("https://pub-123.r2.dev")]
    [InlineData("http://media.example")]
    [InlineData("https://media.example/clips")]
    [InlineData("media.example")]
    public void Options_RefuseAMediaUrlThatIsntTheMediaDomain(string mediaBaseUrl)
    {
        Assert.Throws<InvalidOperationException>(() => R2Options.FromConfiguration(Configuration(
            ("R2:AccountId", "acct"), ("R2:AccessKeyId", "id"), ("R2:SecretAccessKey", "secret"),
            ("R2:Bucket", "bucket"), ("R2:MediaBaseUrl", mediaBaseUrl))));
    }

    [Fact]
    public void Options_RefuseAPartialConfiguration()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            R2Options.FromConfiguration(Configuration(("R2:AccountId", "acct"), ("R2:Bucket", "bucket"))));

        Assert.Contains("R2:SecretAccessKey", error.Message);
        Assert.Contains("R2:MediaBaseUrl", error.Message);
    }

    [Theory]
    [InlineData("av01.0.08M.08,opus", "av01.0.08M.08,opus")]
    [InlineData(" av01.0.08M.08 , opus ", "av01.0.08M.08,opus")]
    [InlineData("avc1.640028,mp4a.40.2", "avc1.640028,mp4a.40.2")]
    [InlineData("av01\"; x=\"y", null)]
    [InlineData("", null)]
    [InlineData("av01,,opus", null)]
    public void Codecs_AreKeptOnlyWhenTheyAreAnRfc6381List(string reported, string? expected)
    {
        Assert.Equal(expected, TranscodeOutputs.NormalizeCodecs(reported));
    }

    private static string[] Sorted(IEnumerable<string> values) => values.Order(StringComparer.Ordinal).ToArray();

    private static IConfiguration Configuration(params (string Key, string Value)[] values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(value => new KeyValuePair<string, string?>(value.Key, value.Value)))
            .Build();
    }
}
