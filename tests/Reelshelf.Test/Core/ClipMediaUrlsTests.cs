using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Reelshelf.Bunny;
using Reelshelf.Core;
using Reelshelf.Core.Models;
using Reelshelf.Video;
using Xunit;

namespace Reelshelf.Test.Core;

public class ClipMediaUrlsTests
{
    private static readonly Guid VideoId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    public static ClipMediaUrls Create(string cdnBaseUrl)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BunnyCdnBaseUrl"] = cdnBaseUrl
            })
            .Build();
        ServiceCollection services = new();
        services.AddKeyedSingleton<IClipVideoMediaUrls>(VideoHostNames.Bunny, new BunnyMediaUrls(configuration));
        services.AddKeyedSingleton<IClipVideoMediaUrls>(VideoHostNames.R2, new FakeMediaUrls());
        return new ClipMediaUrls(services.BuildServiceProvider());
    }

    [Theory]
    [InlineData("https://cdn.example")]
    [InlineData("https://cdn.example/")]
    [InlineData("https://cdn.example//")]
    public void DetectionFrames_AreTheSixBunnyThumbnails(string cdnBaseUrl)
    {
        List<string> urls = Create(cdnBaseUrl).DetectionFrames(VideoHostNames.Bunny, VideoId);

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

        Assert.Equal(new Uri($"https://cdn.example/{VideoId}/playlist.m3u8"), urls.Playlist(VideoHostNames.Bunny, VideoId));
        Assert.Equal($"https://cdn.example/{VideoId}/thumbnail.jpg", urls.Thumbnail(VideoHostNames.Bunny, VideoId));
    }

    [Fact]
    public void EveryUrl_ComesFromTheClipsOwnHost()
    {
        ClipMediaUrls urls = Create("https://cdn.example");

        Assert.Equal([new PlaybackSource($"https://other.example/{VideoId}.mp4", "video/mp4")],
            urls.Sources(VideoHostNames.R2, VideoId));
        Assert.Equal(new Uri($"https://other.example/{VideoId}.m3u8"), urls.Playlist(VideoHostNames.R2, VideoId));
        Assert.Equal($"https://other.example/{VideoId}.jpg", urls.Thumbnail(VideoHostNames.R2, VideoId));
        Assert.Equal([$"https://other.example/{VideoId}/frame.jpg"], urls.DetectionFrames(VideoHostNames.R2, VideoId));
    }

    [Fact]
    public void AnUnregisteredHost_Throws()
    {
        ClipMediaUrls urls = Create("https://cdn.example");

        Assert.Throws<InvalidOperationException>(() => urls.Thumbnail("elsewhere", VideoId));
    }

    private sealed class FakeMediaUrls : IClipVideoMediaUrls
    {
        public List<PlaybackSource> Sources(Guid videoId) => [new($"https://other.example/{videoId}.mp4", "video/mp4")];

        public Uri Playlist(Guid videoId) => new($"https://other.example/{videoId}.m3u8");

        public string Thumbnail(Guid videoId) => $"https://other.example/{videoId}.jpg";

        public List<string> DetectionFrames(Guid videoId) => [$"https://other.example/{videoId}/frame.jpg"];
    }

    [Fact]
    public void Sources_AreTheHlsPlaylistAloneForABunnyVideo()
    {
        List<PlaybackSource> sources = Create("https://cdn.example/").Sources(VideoHostNames.Bunny, VideoId);

        PlaybackSource source = Assert.Single(sources);
        Assert.Equal($"https://cdn.example/{VideoId}/playlist.m3u8", source.Url);
        Assert.Equal("application/vnd.apple.mpegurl", source.Type);
        Assert.Null(source.Height);
    }
}
