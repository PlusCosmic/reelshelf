using Microsoft.Extensions.Configuration;
using Reelshelf.Core;
using Reelshelf.Core.Models;
using Xunit;

namespace Reelshelf.Test.Core;

public class ClipMediaUrlsTests
{
    private static readonly Guid VideoId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static ClipMediaUrls Create(string cdnBaseUrl)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BunnyCdnBaseUrl"] = cdnBaseUrl
            })
            .Build();
        return new ClipMediaUrls(configuration);
    }

    [Theory]
    [InlineData("https://cdn.example")]
    [InlineData("https://cdn.example/")]
    [InlineData("https://cdn.example//")]
    public void DetectionFrames_AreTheSixBunnyThumbnails(string cdnBaseUrl)
    {
        List<string> urls = Create(cdnBaseUrl).DetectionFrames(VideoId);

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

        Assert.Equal(new Uri($"https://cdn.example/{VideoId}/playlist.m3u8"), urls.Playlist(VideoId));
        Assert.Equal($"https://cdn.example/{VideoId}/thumbnail.jpg", urls.Thumbnail(VideoId));
    }

    [Fact]
    public void Sources_AreTheHlsPlaylistAloneForABunnyVideo()
    {
        List<PlaybackSource> sources = Create("https://cdn.example/").Sources(VideoId);

        PlaybackSource source = Assert.Single(sources);
        Assert.Equal($"https://cdn.example/{VideoId}/playlist.m3u8", source.Url);
        Assert.Equal("application/vnd.apple.mpegurl", source.Type);
        Assert.Null(source.Height);
    }
}
