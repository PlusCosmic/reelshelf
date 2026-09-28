using Reelshelf.ApexLegends.LegendDetection;
using Reelshelf.Bunny.Models;
using Reelshelf.ClipTranscription;
using Xunit;

namespace Reelshelf.Test.ClipTranscription;

public class ClipTranscriptionQueueTests
{
    private static readonly ClipTranscriptionOptions Enabled = new() { AutoQueue = true, ApiKey = "key" };

    [Theory]
    [InlineData((int)BunnyVideoStatus.Finished)]
    [InlineData((int)BunnyVideoStatus.ResolutionFinished)]
    public void ShouldAutoQueue_EncodedClipsOfWhitelistedOwners(int status)
    {
        Assert.True(ClipTranscriptionService.ShouldAutoQueue(Enabled, status, ownerWhitelisted: true));
    }

    [Fact]
    public void ShouldAutoQueue_SkipsOwnersOffTheWhitelist()
    {
        Assert.False(ClipTranscriptionService.ShouldAutoQueue(
            Enabled, (int)BunnyVideoStatus.Finished, ownerWhitelisted: false));
    }

    [Theory]
    [InlineData((int)BunnyVideoStatus.Encoding)]
    [InlineData((int)BunnyVideoStatus.Failed)]
    [InlineData(null)]
    public void ShouldAutoQueue_SkipsClipsThatAreNotEncoded(int? status)
    {
        Assert.False(ClipTranscriptionService.ShouldAutoQueue(Enabled, status, ownerWhitelisted: true));
    }

    [Fact]
    public void ShouldAutoQueue_SkipsWhenOffOrNotConfigured()
    {
        const int finished = (int)BunnyVideoStatus.Finished;
        Assert.False(ClipTranscriptionService.ShouldAutoQueue(
            new ClipTranscriptionOptions { AutoQueue = false, ApiKey = "key" }, finished, true));
        Assert.False(ClipTranscriptionService.ShouldAutoQueue(
            new ClipTranscriptionOptions { AutoQueue = true, ApiKey = "" }, finished, true));
        Assert.False(ClipTranscriptionService.ShouldAutoQueue(
            new ClipTranscriptionOptions { AutoQueue = true, ApiKey = "key", Model = " " }, finished, true));
    }

    [Fact]
    public void Prompt_NamesTheGame_AndAddsLegendsForApex()
    {
        TranscriptionPrompt apex = TranscriptionPrompt.For("Apex Legends", "apex-legends");
        Assert.Contains("Apex Legends", apex.Prompt);
        Assert.Equal("Apex Legends", apex.Keywords[0]);
        Assert.Equal(ApexLegendNames.All.Count + 1, apex.Keywords.Count);
        Assert.Contains("Mad Maggie", apex.Keywords);

        TranscriptionPrompt cod = TranscriptionPrompt.For("Call of Duty: Warzone", "call-of-duty-warzone");
        Assert.Equal(["Call of Duty: Warzone"], cod.Keywords);
    }

    [Fact]
    public void Prompt_StripsCharactersKeywordsMayNotContain_AndHandlesNoGame()
    {
        Assert.Equal(["Game Name"],TranscriptionPrompt.For("<Game\r\nName>", "custom").Keywords);

        TranscriptionPrompt unknown = TranscriptionPrompt.For(null, null);
        Assert.Contains("a video game", unknown.Prompt);
        Assert.Empty(unknown.Keywords);
    }
}
