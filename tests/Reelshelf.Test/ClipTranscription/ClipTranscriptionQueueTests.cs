using Reelshelf.ApexLegends.LegendDetection;
using Reelshelf.Bunny.Models;
using Reelshelf.ClipTranscription;
using Xunit;

namespace Reelshelf.Test.ClipTranscription;

public class ClipTranscriptionQueueTests
{
    private const string Gemini = "google/gemini-3.5-transcribe";

    private static readonly ClipTranscriptionOptions Enabled = new() { AutoQueue = true, OpenRouterApiKey = "key" };

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
            new ClipTranscriptionOptions { AutoQueue = false, OpenRouterApiKey = "key" }, finished, true));
        Assert.False(ClipTranscriptionService.ShouldAutoQueue(
            new ClipTranscriptionOptions { AutoQueue = true, OpenRouterApiKey = "" }, finished, true));
        Assert.False(ClipTranscriptionService.ShouldAutoQueue(
            new ClipTranscriptionOptions { AutoQueue = true, OpenRouterApiKey = "key", Model = " " }, finished, true));
    }

    [Fact]
    public void Vocabulary_NamesTheGame_AndAddsLegendsForApex()
    {
        TranscriptionPrompt apex = TranscriptionPrompt.For("Apex Legends", "apex-legends", Gemini);
        Assert.Equal("Apex Legends", apex.Keywords[0]);
        Assert.Equal(ApexLegendNames.All.Count + 1, apex.Keywords.Count);
        Assert.Contains("Mad Maggie", apex.Keywords);

        TranscriptionPrompt cod = TranscriptionPrompt.For("Call of Duty: Warzone", "call-of-duty-warzone", Gemini);
        Assert.Equal(["Call of Duty: Warzone"], cod.Keywords);
    }

    [Fact]
    public void Vocabulary_StripsCharactersKeywordsMayNotContain_AndHandlesNoGame()
    {
        Assert.Equal(["Game Name"], TranscriptionPrompt.For("<Game\r\nName>", "custom", Gemini).Keywords);
        Assert.Empty(TranscriptionPrompt.For(null, null, Gemini).Keywords);
    }

    [Theory]
    [InlineData(Gemini, false, true)]
    [InlineData("openai/gpt-transcribe", true, true)]
    [InlineData("openai/gpt-4o-transcribe", true, false)]
    [InlineData("deepgram/nova-3", false, false)]
    public void Prompt_CarriesOnlyWhatTheModelIsSent(string model, bool hasPrompt, bool hasKeywords)
    {
        TranscriptionPrompt prompt = TranscriptionPrompt.For("Apex Legends", "apex-legends", model);

        Assert.Equal(hasPrompt, prompt.Prompt is not null);
        if (hasPrompt)
        {
            Assert.Contains("Apex Legends", prompt.Prompt);
        }

        Assert.Equal(hasKeywords, prompt.Keywords.Count > 0);
    }

    [Fact]
    public void Defaults_AreGeminiWithDeepgramFallback_BothPriced()
    {
        ClipTranscriptionOptions options = new();

        Assert.Equal(Gemini, options.Model);
        Assert.Equal("deepgram/nova-3", options.EffectiveFallbackModel);
        Assert.True(options.CostPerMinuteUsd.ContainsKey(options.Model));
        Assert.True(options.CostPerMinuteUsd.ContainsKey(options.FallbackModel));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(Gemini)]
    public void FallbackIsOff_WhenEmptyOrTheSameModel(string fallback)
    {
        Assert.Null(new ClipTranscriptionOptions { FallbackModel = fallback }.EffectiveFallbackModel);
    }

    [Fact]
    public void EstimateCost_AddsFallbackMinutes_AtTheFallbackPrice()
    {
        Dictionary<string, decimal> prices = new ClipTranscriptionOptions().CostPerMinuteUsd;
        ClipTranscriptionStatements.UsageRow row = new()
        {
            Model = Gemini, AudioSeconds = 600, FallbackModel = "deepgram/nova-3", FallbackAudioSeconds = 120
        };

        // 10 minutes of Gemini at $0.003 plus 2 minutes of Deepgram at $0.0043.
        Assert.Equal(0.0386m, ClipTranscriptionService.EstimateCost(prices, row));

        row.FallbackModel = "unpriced/model";
        Assert.Null(ClipTranscriptionService.EstimateCost(prices, row));
    }
}
