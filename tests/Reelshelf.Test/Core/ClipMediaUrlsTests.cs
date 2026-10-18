using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Reelshelf.Bunny;
using Reelshelf.Core;
using Reelshelf.Core.Models;
using Reelshelf.Video;
using Reelshelf.Video.R2;
using Xunit;

namespace Reelshelf.Test.Core;

public class ClipMediaUrlsTests
{
    private static readonly Guid VideoId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static ClipVideoRef Bunny => new(VideoHostNames.Bunny, VideoId);

    public static ClipMediaUrls Create(string cdnBaseUrl, string mediaBaseUrl = "https://media.example")
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BunnyCdnBaseUrl"] = cdnBaseUrl
            })
            .Build();
        ServiceCollection services = new();
        services.AddKeyedSingleton<IClipVideoMediaUrls>(VideoHostNames.Bunny, new BunnyMediaUrls(configuration));
        services.AddKeyedSingleton<IClipVideoMediaUrls>(VideoHostNames.R2,
            new R2MediaUrls(new R2Options { MediaBaseUrl = mediaBaseUrl }));
        return new ClipMediaUrls(services.BuildServiceProvider());
    }

    [Theory]
    [InlineData("https://cdn.example")]
    [InlineData("https://cdn.example/")]
    [InlineData("https://cdn.example//")]
    public void DetectionFrames_AreTheSixBunnyThumbnails(string cdnBaseUrl)
    {
        List<string> urls = Create(cdnBaseUrl).DetectionFrames(Bunny);

        Assert.Equal(
        [
            $"https://cdn.example/{VideoId}/thumbnail.jpg",
            $"https://cdn.example/{VideoId}/thumbnail_1.jpg",
            $"https://cdn.example/{VideoId}/thumbnail_2.jpg",
            $"https://cdn.example/{VideoId}/thumbnail_3.jpg",
            $"https://cdn.example/{VideoId}/thumbnail_4.jpg",
            $"https://cdn.example/{VideoId}/thumbnail_5.jpg"
        ], urls);
    }

    [Theory]
    [InlineData("https://cdn.example")]
    [InlineData("https://cdn.example/")]
    public void PlaylistAndThumbnail_TrimTheBaseUrlsTrailingSlash(string cdnBaseUrl)
    {
        ClipMediaUrls urls = Create(cdnBaseUrl);

        Assert.Equal(new Uri($"https://cdn.example/{VideoId}/playlist.m3u8"), urls.ServerStream(Bunny));
        Assert.Equal($"https://cdn.example/{VideoId}/thumbnail.jpg", urls.Thumbnail(Bunny));
        Assert.Equal(VideoId.ToString(), urls.MediaPath(Bunny));
    }

    [Fact]
    public void Sources_AreTheHlsPlaylistAloneForABunnyVideo()
    {
        // A version, codecs and height on a Bunny clip change nothing.
        List<PlaybackSource> sources = Create("https://cdn.example/")
            .Sources(Bunny with { VideoVersion = 3, Codecs = "av01.0.08M.08,opus", Height = 1080 });

        PlaybackSource source = Assert.Single(sources);
        Assert.Equal($"https://cdn.example/{VideoId}/playlist.m3u8", source.Url);
        Assert.Equal("application/vnd.apple.mpegurl", source.Type);
        Assert.Null(source.Height);
    }

    [Theory]
    [InlineData("https://media.example")]
    [InlineData("https://media.example/")]
    public void AnR2Clip_IsServedFromTheMediaDomain_UnderItsCurrentVersion(string mediaBaseUrl)
    {
        ClipMediaUrls urls = Create("https://cdn.example", mediaBaseUrl);
        ClipVideoRef video = new(VideoHostNames.R2, VideoId, VideoVersion: 2, Codecs: "av01.0.08M.08,opus", Height: 1080);
        string folder = $"https://media.example/clips/{VideoId}/v2";

        PlaybackSource source = Assert.Single(urls.Sources(video));
        Assert.Equal(new PlaybackSource($"{folder}/av1.mp4", "video/mp4; codecs=\"av01.0.08M.08,opus\"", 1080), source);
        Assert.Equal(new Uri($"{folder}/av1.mp4"), urls.ServerStream(video));
        Assert.Equal($"{folder}/thumbnail.jpg", urls.Thumbnail(video));
        Assert.Equal(
        [
            $"{folder}/thumbnail.jpg",
            $"{folder}/thumbnail_1.jpg",
            $"{folder}/thumbnail_2.jpg",
            $"{folder}/thumbnail_3.jpg",
            $"{folder}/thumbnail_4.jpg",
            $"{folder}/thumbnail_5.jpg"
        ], urls.DetectionFrames(video));
        Assert.Equal($"clips/{VideoId}/v2", urls.MediaPath(video));
    }

    [Fact]
    public void AnR2ClipWithoutCodecs_IsPlainMp4()
    {
        PlaybackSource source = Assert.Single(Create("https://cdn.example").Sources(new ClipVideoRef(VideoHostNames.R2, VideoId)));

        Assert.Equal($"https://media.example/clips/{VideoId}/v1/av1.mp4", source.Url);
        Assert.Equal("video/mp4", source.Type);
        Assert.Null(source.Height);
    }

    [Fact]
    public void AnUnregisteredHost_Throws()
    {
        ClipMediaUrls urls = Create("https://cdn.example");

        Assert.Throws<InvalidOperationException>(() => urls.Thumbnail(new ClipVideoRef("elsewhere", VideoId)));
    }
}
