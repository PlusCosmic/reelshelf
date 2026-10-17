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
            new HostedVideoInfo("Triple kill", 42, (int)BunnyVideoObjectStatus.Transcoding, 55, 123456, uploaded, "thumbnail.jpg"),
            info);
    }
}
