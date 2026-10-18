using Xunit;

namespace Reelshelf.Worker.Test;

/// <summary>How a <c>migrate</c> job's remux is judged from ffmpeg's exit and log, with lines as ffmpeg writes them.</summary>
public class HlsRemuxTests
{
    [Theory]
    [InlineData(false, "[in#0 @ 0x55] Error opening input: Server returned 404 Not Found")]
    [InlineData(false, "[in#0 @ 0x55] Error opening input: Server returned 403 Forbidden (access denied)")]
    [InlineData(true, "[http @ 0x55] HTTP error 404 Not Found", "[hls @ 0x55] Failed to open segment 2 of playlist 0")]
    [InlineData(false, "[http @ 0x55] HTTP error 410 Gone")]
    public void APlaylistOrSegmentThatIsGone_IsNotRetried(bool exitedCleanly, params string[] lines)
    {
        Assert.Equal(HlsRemux.Outcome.Gone, HlsRemux.Classify(exitedCleanly, lines));
    }

    [Theory]
    [InlineData(false, "[in#0 @ 0x55] Error opening input: Server returned 5XX Server Error reply")]
    [InlineData(false, "[in#0 @ 0x55] Error opening input: Connection refused")]
    [InlineData(false, "[tls @ 0x55] Error in the pull function.")]
    [InlineData(true, "[http @ 0x55] HTTP error 503 Service Unavailable",
        "[hls @ 0x55] Segment 2 of playlist 0 failed too many times, skipping")]
    [InlineData(true, "[hls @ 0x55] Failed to reload playlist 0")]
    public void NetworkErrorsAndSkippedSegments_AreRetried(bool exitedCleanly, params string[] lines)
    {
        Assert.Equal(HlsRemux.Outcome.Transient, HlsRemux.Classify(exitedCleanly, lines));
    }

    [Fact]
    public void InputThatIsntHls_IsNotRetried()
    {
        Assert.Equal(HlsRemux.Outcome.Unreadable, HlsRemux.Classify(false,
            ["[in#0 @ 0x55] Error opening input: Invalid data found when processing input"]));
    }

    [Fact]
    public void ACleanExit_WithOnlyHarmlessWarnings_IsComplete()
    {
        Assert.Equal(HlsRemux.Outcome.Complete, HlsRemux.Classify(true, []));
        Assert.Equal(HlsRemux.Outcome.Complete, HlsRemux.Classify(true,
            ["[mp4 @ 0x55] Non-monotonous DTS in output stream 0:1; previous: 1, current: 0; changing to 2."]));
    }

    [Fact]
    public void TheRemux_CopiesFromHlsOverTheNetworkOnly()
    {
        IReadOnlyList<string> args = EncodeArguments.HlsRemux("https://cdn.example/v/playlist.m3u8", "/work/source");

        Assert.Equal(["-f", "hls", "-i", "https://cdn.example/v/playlist.m3u8"],
            args.SkipWhile(arg => arg != "-f").Take(4));
        Assert.Equal("https,http,tls,tcp,crypto", args[args.ToList().IndexOf("-protocol_whitelist") + 1]);
        Assert.Contains("copy", args);
        Assert.Equal("/work/source", args[^1]);
    }
}
