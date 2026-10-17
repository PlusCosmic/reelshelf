using Reelshelf.Bunny.Models;
using Reelshelf.Core;
using Reelshelf.Video;
using Xunit;

namespace Reelshelf.Test.Core;

public class AbandonedUploadStatusTests
{
    // Bunny's video object statuses, not the webhook's.
    [Theory]
    [InlineData((int)BunnyVideoObjectStatus.Created)]
    [InlineData((int)BunnyVideoObjectStatus.UploadFailed)]
    [InlineData((int)BunnyVideoObjectStatus.Error)]
    public void Never_uploaded_or_errored_videos_can_be_purged(int status)
    {
        Assert.False(ClipStatusRefreshService.IsUploaded(status));
    }

    // The old check read 8 (JitPlaylistsCreated) and 6 as the webhook's pre-upload states.
    [Theory]
    [InlineData((int)BunnyVideoObjectStatus.Uploaded)]
    [InlineData((int)BunnyVideoObjectStatus.Processing)]
    [InlineData((int)BunnyVideoObjectStatus.Transcoding)]
    [InlineData((int)BunnyVideoObjectStatus.Finished)]
    [InlineData((int)BunnyVideoObjectStatus.JitSegmenting)]
    [InlineData((int)BunnyVideoObjectStatus.JitPlaylistsCreated)]
    public void Uploaded_videos_are_kept(int status)
    {
        Assert.True(ClipStatusRefreshService.IsUploaded(status));
    }

    [Fact]
    public void A_video_the_host_no_longer_has_is_abandoned()
    {
        Assert.True(ClipStatusRefreshService.IsAbandoned(null));
    }

    [Fact]
    public void An_empty_never_uploaded_video_is_abandoned()
    {
        Assert.True(ClipStatusRefreshService.IsAbandoned(Video((int)BunnyVideoObjectStatus.Created, storageSize: 0)));
    }

    // A clip whose update notification was missed is refreshed by the status pass, not deleted.
    [Theory]
    [InlineData((int)BunnyVideoObjectStatus.Finished, 0L)]
    [InlineData((int)BunnyVideoObjectStatus.Error, 1024L)]
    public void A_video_with_bytes_or_an_upload_status_is_kept(int status, long storageSize)
    {
        Assert.False(ClipStatusRefreshService.IsAbandoned(Video(status, storageSize)));
    }

    private static HostedVideoInfo Video(int status, long storageSize)
    {
        return new HostedVideoInfo("clip", 0, status, 0, storageSize, DateTimeOffset.UnixEpoch, null);
    }
}
