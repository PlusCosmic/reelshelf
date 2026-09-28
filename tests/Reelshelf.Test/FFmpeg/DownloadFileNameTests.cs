using Reelshelf.FFmpeg;
using Xunit;

namespace Reelshelf.Test.FFmpeg;

public class DownloadFileNameTests
{
    private static readonly Guid VideoId = Guid.Parse("80bfc1f6-7cd8-4d58-beb0-7a4659d9ac21");

    [Theory]
    [InlineData("Marvel Rivals 2026.05.09 - 17.09.03.02.DVR", "Marvel Rivals 2026.05.09 - 17.09.03.02.DVR.mp4")]
    [InlineData("Apex Legends 2026.01.10 - 21.50.55.16.DVR.mp4", "Apex Legends 2026.01.10 - 21.50.55.16.DVR.mp4")]
    [InlineData("Clutch: 1v3 / final ring?", "Clutch 1v3 final ring.mp4")]
    [InlineData("  spaced   out  ", "spaced out.mp4")]
    public void UsesTheTitle_MadeSafe(string title, string expected)
    {
        Assert.Equal(expected, DownloadFileName.For(title, VideoId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("///")]
    public void FallsBackToTheVideoId_WhenNothingIsLeft(string? title)
    {
        Assert.Equal($"{VideoId}.mp4", DownloadFileName.For(title, VideoId));
    }

    [Fact]
    public void KeepsLongTitlesToAReasonableLength()
    {
        string name = DownloadFileName.For(new string('a', 400), VideoId);
        Assert.Equal(150 + ".mp4".Length, name.Length);
    }
}
