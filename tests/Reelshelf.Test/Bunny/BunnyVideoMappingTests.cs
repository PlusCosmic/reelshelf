using Reelshelf.Bunny;
using Reelshelf.Bunny.Models;
using Reelshelf.Video;
using Xunit;

namespace Reelshelf.Test.Bunny;

public class BunnyVideoMappingTests
{
    [Fact]
    public void ToHostedVideoInfo_CarriesWhatClipsStore()
    {
        DateTimeOffset uploaded = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        BunnyVideo video = new(
            VideoLibraryId: 1,
            Guid: Guid.NewGuid(),
            Title: "Triple kill",
            DateUploaded: uploaded,
            Length: 42,
            Status: (int)BunnyVideoObjectStatus.Transcoding,
            Framerate: 60,
            ThumbnailCount: 6,
            EncodeProgress: 55,
            StorageSize: 123456,
            CollectionId: Guid.NewGuid(),
            ThumbnailFileName: "thumbnail.jpg",
            ThumbnailBlurhash: "",
            Category: "",
            Moments: [],
            MetaTags: []);

        HostedVideoInfo info = BunnyService.ToHostedVideoInfo(video);

        Assert.Equal(
            new HostedVideoInfo(
                "Triple kill", 42, ClipVideoStatus.Processing, 55, 123456, uploaded, "thumbnail.jpg",
                HostStatus: (int)BunnyVideoObjectStatus.Transcoding),
            info);
    }

    // Must match the backfill in V38__clip_video_state.sql.
    [Theory]
    [InlineData(BunnyVideoObjectStatus.Created, ClipVideoStatus.AwaitingUpload)]
    [InlineData(BunnyVideoObjectStatus.Uploaded, ClipVideoStatus.Uploaded)]
    [InlineData(BunnyVideoObjectStatus.Processing, ClipVideoStatus.Processing)]
    [InlineData(BunnyVideoObjectStatus.Transcoding, ClipVideoStatus.Processing)]
    [InlineData(BunnyVideoObjectStatus.Finished, ClipVideoStatus.Ready)]
    [InlineData(BunnyVideoObjectStatus.Error, ClipVideoStatus.EncodeFailed)]
    [InlineData(BunnyVideoObjectStatus.UploadFailed, ClipVideoStatus.UploadFailed)]
    [InlineData(BunnyVideoObjectStatus.JitSegmenting, ClipVideoStatus.Processing)]
    [InlineData(BunnyVideoObjectStatus.JitPlaylistsCreated, ClipVideoStatus.Processing)]
    [InlineData((BunnyVideoObjectStatus)42, ClipVideoStatus.Processing)]
    public void BunnyStatuses_MapOntoTheClipsOwn(BunnyVideoObjectStatus bunny, ClipVideoStatus expected)
    {
        Assert.Equal(expected, BunnyService.ToClipVideoStatus((int)bunny));
    }

    // What the app did with Bunny's numbers before it had its own statuses: only Finished played, and only
    // Created, UploadFailed and Error counted as never uploaded.
    [Theory]
    [InlineData(BunnyVideoObjectStatus.Created, false, false)]
    [InlineData(BunnyVideoObjectStatus.Uploaded, false, true)]
    [InlineData(BunnyVideoObjectStatus.Processing, false, true)]
    [InlineData(BunnyVideoObjectStatus.Transcoding, false, true)]
    [InlineData(BunnyVideoObjectStatus.Finished, true, true)]
    [InlineData(BunnyVideoObjectStatus.Error, false, false)]
    [InlineData(BunnyVideoObjectStatus.UploadFailed, false, false)]
    [InlineData(BunnyVideoObjectStatus.JitSegmenting, false, true)]
    [InlineData(BunnyVideoObjectStatus.JitPlaylistsCreated, false, true)]
    [InlineData((BunnyVideoObjectStatus)42, false, true)]
    public void Mapping_KeepsPlayableAndUploadedAsBefore(BunnyVideoObjectStatus bunny, bool playable, bool uploaded)
    {
        ClipVideoStatus status = BunnyService.ToClipVideoStatus((int)bunny);
        Assert.Equal(playable, status.IsPlayable());
        Assert.Equal(uploaded, status.IsUploaded());
    }
}
