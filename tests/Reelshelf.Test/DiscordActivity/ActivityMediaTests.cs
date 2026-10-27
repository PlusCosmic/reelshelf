using Reelshelf.Discord.DiscordActivity;
using Reelshelf.Test.Core;
using Reelshelf.Video;
using Xunit;

namespace Reelshelf.Test.DiscordActivity;

public class ActivityMediaTests
{
    private static readonly Guid VideoId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public void AReadyClip_CarriesItsStreamTypeAndThumbnail()
    {
        ActivityMedia media = ActivityMedia.For(ClipMediaUrlsTests.Create("https://cdn.example"),
            new ClipVideoRef(VideoHostNames.R2, VideoId, VideoVersion: 2, Codecs: "av01.0.08M.08,opus"));

        Assert.Equal(new ActivityMedia("r2", $"clips/{VideoId}/v2", "video/mp4; codecs=\"av01.0.08M.08,opus\"", true), media);
    }

    [Fact]
    public void AClipStillProcessing_HasNoStream_AndAThumbnailOnlyOnceItExists()
    {
        ClipVideoRef video = new(VideoHostNames.R2, VideoId, Status: ClipVideoStatus.Processing);

        ActivityMedia before = ActivityMedia.For(ClipMediaUrlsTests.Create("https://cdn.example"), video);
        ActivityMedia after = ActivityMedia.For(ClipMediaUrlsTests.Create("https://cdn.example"),
            video with { ThumbnailsReady = true });

        Assert.Equal(new ActivityMedia("r2", $"clips/{VideoId}/v1", null, false), before);
        Assert.Equal(new ActivityMedia("r2", $"clips/{VideoId}/v1", null, true), after);
    }
}
