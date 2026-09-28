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
        TranscriptionPrompt apex = TranscriptionPrompt.For("Apex Legends", "apex-legends", "gpt-transcribe");
        Assert.Contains("Apex Legends", apex.Prompt);
        Assert.Equal("Apex Legends", apex.Keywords[0]);
        Assert.Equal(ApexLegendNames.All.Count + 1, apex.Keywords.Count);
        Assert.Contains("Mad Maggie", apex.Keywords);

        TranscriptionPrompt cod = TranscriptionPrompt.For("Call of Duty: Warzone", "call-of-duty-warzone", "gpt-transcribe");
        Assert.Equal(["Call of Duty: Warzone"], cod.Keywords);
    }

    [Fact]
    public void Prompt_StripsCharactersKeywordsMayNotContain_AndHandlesNoGame()
    {
        Assert.Equal(["Game Name"],TranscriptionPrompt.For("<Game\r\nName>", "custom", "gpt-transcribe").Keywords);

        TranscriptionPrompt unknown = TranscriptionPrompt.For(null, null, "gpt-transcribe");
        Assert.Contains("a video game", unknown.Prompt);
        Assert.Empty(unknown.Keywords);
    }

    [Theory]
    [InlineData("gpt-4o-transcribe")]
    [InlineData("gpt-4o-mini-transcribe")]
    [InlineData("whisper-1")]
    public void Prompt_SendsNoKeywords_ToModelsThatDontTakeThem(string model)
    {
        TranscriptionPrompt prompt = TranscriptionPrompt.For("Apex Legends", "apex-legends", model);

        Assert.Contains("Apex Legends", prompt.Prompt);
        Assert.Empty(prompt.Keywords);
    }

    [Fact]
    public void DefaultModel_IsGpt4oTranscribe_AndHasAPrice()
    {
        ClipTranscriptionOptions options = new();

        Assert.Equal("gpt-4o-transcribe", options.Model);
        Assert.Equal(0.006m, options.CostPerMinuteUsd[options.Model]);
    }
}
