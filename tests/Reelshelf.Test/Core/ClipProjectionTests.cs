using Microsoft.Extensions.Configuration;
using Reelshelf.Core;
using Reelshelf.Core.Models;
using Reelshelf.Games;
using Reelshelf.Video;
using Xunit;

namespace Reelshelf.Test.Core;

public class ClipProjectionTests
{
    private static readonly Guid VideoId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly DateTimeOffset CreatedAt = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly GameCategory Category = new()
    {
        Id = Guid.Parse("66666666-7777-8888-9999-000000000000"),
        Name = "Apex Legends",
        Slug = "apex-legends"
    };

    private static ClipProjection Create()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BunnyCdnBaseUrl"] = "https://cdn.example/"
            })
            .Build();
        return new ClipProjection(new ClipMediaUrls(configuration));
    }

    private static ClipsStatements.ClipWithTagsRow Row() => new()
    {
        Id = Guid.NewGuid(),
        OwnerId = Guid.NewGuid(),
        VideoId = VideoId,
        GameCategoryId = Category.Id,
        CreatedAt = CreatedAt,
        TagNames = "clutch,squad wipe",
        Title = "Third party",
        Length = 42,
        ThumbnailFileName = "thumbnail.jpg",
        DateUploaded = CreatedAt.AddMinutes(1),
        StorageSize = 1_234_567,
        VideoState = ClipVideoStatus.Processing.ToDatabase(),
        EncodeProgress = 35
    };

    [Fact]
    public void ProjectClip_FillsTheVideoAndItsUrls()
    {
        ClipsStatements.ClipWithTagsRow row = Row();

        Clip clip = Create().ProjectClip(row, Category, isViewed: true, isShared: false);

        Assert.Equal(row.Id, clip.ClipId);
        Assert.Equal("apex-legends", clip.CategorySlug);
        Assert.Equal(["clutch", "squad wipe"], clip.Tags);
        Assert.True(clip.IsViewed);
        Assert.False(clip.Share.Shared);
        Assert.Equal(
            [new PlaybackSource($"https://cdn.example/{VideoId}/playlist.m3u8", "application/vnd.apple.mpegurl")],
            clip.Video.Sources);
        // Records compare lists by reference, so the sources are checked above and shared here.
        List<PlaybackSource> sources = clip.Video.Sources;
        Assert.Equal(
            new ClipVideo(
                VideoId,
                "Third party",
                42,
                ClipVideoStatus.Processing,
                35,
                1_234_567,
                CreatedAt.AddMinutes(1),
                $"https://cdn.example/{VideoId}/thumbnail.jpg",
                sources),
            clip.Video);
    }

    [Fact]
    public void ProjectClip_DefaultsAVideoTheHostHasNotDescribedYet()
    {
        ClipsStatements.ClipWithTagsRow row = Row();
        row.Title = null;
        row.Length = null;
        row.StorageSize = null;
        row.EncodeProgress = null;
        row.DateUploaded = null;
        row.VideoState = ClipVideoStatus.AwaitingUpload.ToDatabase();

        ClipVideo video = Create().ProjectClip(row, Category, isViewed: false, isShared: true).Video;

        Assert.Equal("Untitled", video.Title);
        Assert.Equal(0, video.DurationSeconds);
        Assert.Equal(0, video.StorageBytes);
        Assert.Equal(0, video.EncodeProgress);
        Assert.Equal(CreatedAt, video.UploadedAt);
        Assert.Equal(ClipVideoStatus.AwaitingUpload, video.Status);
    }
}
