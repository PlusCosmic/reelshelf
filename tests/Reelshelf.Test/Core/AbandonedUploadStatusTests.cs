using Reelshelf.Core;
using Reelshelf.Video;
using Xunit;

namespace Reelshelf.Test.Core;

public class AbandonedUploadStatusTests
{
    [Theory]
    [InlineData(ClipVideoStatus.AwaitingUpload)]
    [InlineData(ClipVideoStatus.UploadFailed)]
    [InlineData(ClipVideoStatus.EncodeFailed)]
    public void Never_uploaded_or_errored_videos_can_be_purged(ClipVideoStatus status)
    {
        Assert.False(status.IsUploaded());
    }

    [Theory]
    [InlineData(ClipVideoStatus.Uploaded)]
    [InlineData(ClipVideoStatus.Processing)]
    [InlineData(ClipVideoStatus.Ready)]
    public void Uploaded_videos_are_kept(ClipVideoStatus status)
    {
        Assert.True(status.IsUploaded());
    }

    [Fact]
    public void A_video_the_host_no_longer_has_is_abandoned()
    {
        Assert.True(ClipStatusRefreshService.IsAbandoned(null));
    }

    [Fact]
    public void An_empty_never_uploaded_video_is_abandoned()
    {
        Assert.True(ClipStatusRefreshService.IsAbandoned(Video(ClipVideoStatus.AwaitingUpload, storageSize: 0)));
    }

    // A clip whose update notification was missed is refreshed by the status pass, not deleted.
    [Theory]
    [InlineData(ClipVideoStatus.Ready, 0L)]
    [InlineData(ClipVideoStatus.EncodeFailed, 1024L)]
    public void A_video_with_bytes_or_an_upload_status_is_kept(ClipVideoStatus status, long storageSize)
    {
        Assert.False(ClipStatusRefreshService.IsAbandoned(Video(status, storageSize)));
    }

    private static HostedVideoInfo Video(ClipVideoStatus status, long storageSize)
    {
        return new HostedVideoInfo("clip", 0, status, 0, storageSize, DateTimeOffset.UnixEpoch, null, HostStatus: 0);
    }
}
