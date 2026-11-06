using Microsoft.Extensions.DependencyInjection;
using Reelshelf.Core;
using Reelshelf.Core.Models;
using Reelshelf.Video;
using Reelshelf.Video.R2;
using Xunit;

namespace Reelshelf.Test.Core;

public class ClipMediaUrlsTests
{
    private static readonly Guid VideoId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    public static ClipMediaUrls Create(string mediaBaseUrl = "https://media.example")
    {
        ServiceCollection services = new();
        services.AddKeyedSingleton<IClipVideoMediaUrls>(VideoHostNames.R2,
            new R2MediaUrls(new R2Options { MediaBaseUrl = mediaBaseUrl }));
        return new ClipMediaUrls(services.BuildServiceProvider());
    }

    [Theory]
    [InlineData("https://media.example")]
    [InlineData("https://media.example/")]
    public void AnR2Clip_IsServedFromTheMediaDomain_UnderItsCurrentVersion(string mediaBaseUrl)
    {
        ClipMediaUrls urls = Create(mediaBaseUrl);
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
        PlaybackSource source = Assert.Single(Create().Sources(new ClipVideoRef(VideoHostNames.R2, VideoId)));

        Assert.Equal($"https://media.example/clips/{VideoId}/v1/av1.mp4", source.Url);
        Assert.Equal("video/mp4", source.Type);
        Assert.Null(source.Height);
    }

    [Theory]
    [InlineData(ClipVideoStatus.AwaitingUpload)]
    [InlineData(ClipVideoStatus.Uploaded)]
    [InlineData(ClipVideoStatus.Processing)]
    [InlineData(ClipVideoStatus.EncodeFailed)]
    [InlineData(ClipVideoStatus.UploadFailed)]
    public void AnR2ClipThatIsNotReady_HasNoMedia_UntilItsThumbnailsAreReported(ClipVideoStatus status)
    {
        ClipMediaUrls urls = Create();
        ClipVideoRef video = new(VideoHostNames.R2, VideoId, Status: status);

        Assert.Empty(urls.Sources(video));
        Assert.Null(urls.Thumbnail(video));
        Assert.Empty(urls.DetectionFrames(video));
        // The folder is known from the start; only the files in it wait.
        Assert.Equal($"clips/{VideoId}/v1", urls.MediaPath(video));

        ClipVideoRef reported = video with { ThumbnailsReady = true };
        Assert.Empty(urls.Sources(reported));
        Assert.Equal($"https://media.example/clips/{VideoId}/v1/thumbnail.jpg", urls.Thumbnail(reported));
        Assert.Equal(6, urls.DetectionFrames(reported).Count);
    }

    [Fact]
    public void AnUnregisteredHost_Throws()
    {
        ClipMediaUrls urls = Create();

        Assert.Throws<InvalidOperationException>(() => urls.Thumbnail(new ClipVideoRef("elsewhere", VideoId)));
        // Even for a video with no sources to hand out yet.
        Assert.Throws<InvalidOperationException>(() =>
            urls.Sources(new ClipVideoRef("elsewhere", VideoId, Status: ClipVideoStatus.Processing)));
    }
}
