using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Reelshelf.Bunny;
using Reelshelf.Video;
using Xunit;

namespace Reelshelf.Test.Bunny;

public class BunnyUploadTargetTests
{
    private static readonly Guid VideoId = Guid.Parse("4f1c2b8e-9d3a-4e5f-8a6b-7c8d9e0f1a2b");
    private static readonly Guid CollectionId = Guid.Parse("0a1b2c3d-4e5f-6a7b-8c9d-0e1f2a3b4c5d");
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

    private static HostedVideo Video() =>
        new(VideoId, CollectionId, new HostedVideoInfo("Triple kill", 0, 0, 0, 0, Now, null));

    [Fact]
    public void Create_SignsLikeBunnysPresignedTusUpload()
    {
        UploadTarget target = BunnyUploadTarget.Create("12345", "secret-key", Video(), "Triple kill", Now);

        long expiration = Now.AddHours(1).ToUnixTimeSeconds();
        string expected = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes("12345" + "secret-key" + expiration + VideoId)));

        Assert.Equal(expected, target.Headers["AuthorizationSignature"]);
        // Pinned digest for these fixed inputs, worked out independently of the code under test.
        Assert.Equal("5A3B349599E894BE25EBDCF86CFBD9B462F0346F0848D44C37BB1C6CACED7DEB", expected);
        Assert.Equal(1792242000L, expiration);
        Assert.Equal("1792242000", target.Headers["AuthorizationExpire"]);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(expiration), target.ExpiresAt);
    }

    [Fact]
    public void Create_DescribesBunnysTusEndpointHeadersAndMetadata()
    {
        UploadTarget target = BunnyUploadTarget.Create("12345", "secret-key", Video(), "Triple kill", Now);

        Assert.Equal(UploadTarget.Tus, target.Protocol);
        Assert.Equal("https://video.bunnycdn.com/tusupload", target.Endpoint);
        Assert.Equal(
            ["AuthorizationExpire", "AuthorizationSignature", "LibraryId", "VideoId"],
            target.Headers.Keys.Order());
        Assert.Equal(VideoId.ToString(), target.Headers["VideoId"]);
        Assert.Equal("12345", target.Headers["LibraryId"]);
        Assert.Equal(
            new Dictionary<string, string> { ["title"] = "Triple kill", ["collection"] = CollectionId.ToString() },
            target.Metadata);
        Assert.DoesNotContain("secret-key", target.Headers.Values);
    }

    [Fact]
    public void Serialize_KeepsHeaderAndMetadataNamesUnderTheApisSnakeCasePolicy()
    {
        UploadTarget target = BunnyUploadTarget.Create("12345", "secret-key", Video(), "Triple kill", Now);
        JsonSerializerOptions options = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

        JsonElement json = JsonSerializer.SerializeToElement(target, options);

        Assert.True(json.GetProperty("headers").TryGetProperty("AuthorizationSignature", out _));
        Assert.True(json.GetProperty("metadata").TryGetProperty("collection", out _));
        Assert.True(json.TryGetProperty("expires_at", out _));
    }
}
