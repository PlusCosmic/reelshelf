using System.Text.Json;
using Reelshelf.Video;
using Xunit;

namespace Reelshelf.Test.Video;

public class ClipVideoStatusTests
{
    [Fact]
    public void DatabaseSpelling_RoundTrips_AndMatchesTheJson()
    {
        foreach (ClipVideoStatus status in Enum.GetValues<ClipVideoStatus>())
        {
            string stored = status.ToDatabase();
            Assert.Equal(status, ClipVideoStatuses.FromDatabase(stored));
            Assert.Equal($"\"{stored}\"", JsonSerializer.Serialize(status));
        }

        Assert.Equal("awaiting_upload", ClipVideoStatus.AwaitingUpload.ToDatabase());
        Assert.Equal("encode_failed", ClipVideoStatus.EncodeFailed.ToDatabase());
    }

    [Fact]
    public void OnlyReadyIsPlayable()
    {
        Assert.Equal([ClipVideoStatus.Ready], Enum.GetValues<ClipVideoStatus>().Where(s => s.IsPlayable()));
    }
}
