using Reelshelf.Bunny.Models;
using Reelshelf.Core;
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
}
