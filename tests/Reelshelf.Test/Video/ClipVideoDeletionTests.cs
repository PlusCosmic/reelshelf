using Reelshelf.Video;
using Reelshelf.Video.Transcoding;
using Xunit;

namespace Reelshelf.Test.Video;

/// <summary>What deleting one clip deletes at its hosts (<see cref="ClipVideoDeletion"/>).</summary>
public class ClipVideoDeletionTests
{
    [Fact]
    public async Task AMigratedClipInItsGracePeriod_TakesItsBunnyCopyWithIt()
    {
        FakeBunnyHost bunny = new();
        FakeBunnyHost r2 = new();
        RecordingStorage storage = new();
        Guid clipId = Guid.NewGuid();
        Guid bunnyCopy = Guid.NewGuid();

        await ClipVideoDeletion.DeleteAsync(bunny.Hosts(r2), storage, clipId, VideoHostNames.R2, clipId, bunnyCopy);

        Assert.Equal([clipId], r2.DeletedVideos);
        Assert.Equal([bunnyCopy], bunny.DeletedVideos);
        Assert.Empty(storage.DeletedVersions);
    }

    [Fact]
    public async Task AClipWithoutABunnyCopy_DeletesOnlyItsOwnVideo()
    {
        FakeBunnyHost bunny = new();
        FakeBunnyHost r2 = new();
        RecordingStorage storage = new();
        Guid clipId = Guid.NewGuid();

        await ClipVideoDeletion.DeleteAsync(bunny.Hosts(r2), storage, clipId, VideoHostNames.R2, clipId, bunnyVideoId: null);

        Assert.Equal([clipId], r2.DeletedVideos);
        Assert.Empty(bunny.DeletedVideos);
    }

    [Fact]
    public async Task ABunnyClip_DeletesItsVideoAndAnyMigrateOutput_AndAClipSwitchedBackIsNotDeletedTwice()
    {
        FakeBunnyHost bunny = new();
        RecordingStorage storage = new();
        Guid clipId = Guid.NewGuid();
        Guid videoId = Guid.NewGuid();

        // Switched back to its Bunny copy, with bunny_video_id left set.
        await ClipVideoDeletion.DeleteAsync(bunny.Hosts(), storage, clipId, VideoHostNames.Bunny, videoId, videoId);

        Assert.Equal([videoId], bunny.DeletedVideos);
        Assert.Equal([(clipId, TranscodeQueue.MigratedVideoVersion)], storage.DeletedVersions);
    }

    [Fact]
    public async Task WhenTheBunnyCopyFailsToDelete_TheErrorReachesTheCaller_SoTheClipRowIsKept()
    {
        FakeBunnyHost bunny = new();
        Guid clipId = Guid.NewGuid();
        Guid bunnyCopy = Guid.NewGuid();
        bunny.FailingVideos.Add(bunnyCopy);

        await Assert.ThrowsAsync<HttpRequestException>(() => ClipVideoDeletion.DeleteAsync(bunny.Hosts(),
            new RecordingStorage(), clipId, VideoHostNames.R2, clipId, bunnyCopy));
    }

    private sealed class RecordingStorage : ITranscodeStorage
    {
        public List<(Guid ClipId, int Version)> DeletedVersions { get; } = [];

        public TranscodeJobUrls UrlsFor(Guid clipId, string jobType, int videoVersion, int currentVersion, TimeSpan lifetime) =>
            TranscodeJobUrls.None;

        public Task DeleteSourceAsync(Guid clipId) => Task.CompletedTask;

        public Task DeleteVersionAsync(Guid clipId, int videoVersion)
        {
            DeletedVersions.Add((clipId, videoVersion));
            return Task.CompletedTask;
        }
    }
}
