using System.Text.Json;
using Microsoft.Extensions.AI;
using Reelshelf.ClipSummary;
using Xunit;

namespace Reelshelf.Test.ClipSummary;

public class ClipSummaryTests
{
    private const string Transcript = "No way. NO WAY, did you see that?! Jonno, get the banner... I'm out of ammo";

    [Fact]
    public void Parse_KeepsOnlyMoodTagsFromTheList()
    {
        ClipSummaryResult result = ClipSummaryResult.Parse(
            Response(moodTags: ["Hype", "spicy", "funny", "hype"]), Transcript);

        Assert.Equal(["funny", "hype"], result.MoodTags);
    }

    [Fact]
    public void Parse_DropsQuotesThatAreNotInTheTranscript()
    {
        ClipSummaryResult result = ClipSummaryResult.Parse(
            Response(quotes: ["no way, did you see that", "Jonno get the banner", "I'm out of bullets", "  "]),
            Transcript);

        Assert.Equal(["no way, did you see that", "Jonno get the banner"], result.Quotes);
    }

    [Fact]
    public void Parse_DropsEveryQuote_WhenTheTranscriptIsEmpty()
    {
        ClipSummaryResult result = ClipSummaryResult.Parse(Response(quotes: ["gg"]), "");

        Assert.Empty(result.Quotes);
    }

    [Fact]
    public void Parse_TrimsAndDeduplicatesPeople()
    {
        ClipSummaryResult result = ClipSummaryResult.Parse(Response(people: [" Jonno ", "jonno", ""]), Transcript);

        Assert.Equal(["Jonno"], result.People);
    }

    [Fact]
    public void Parse_RejectsAResponseWithoutADescription()
    {
        Assert.ThrowsAny<JsonException>(() => ClipSummaryResult.Parse(Response(description: " "), Transcript));
        Assert.ThrowsAny<JsonException>(() => ClipSummaryResult.Parse("not json", Transcript));
    }

    [Fact]
    public void EmbeddingText_IsTheDescriptionThenTheMoodTags()
    {
        ClipSummaryResult tagged = new("A late win.", ["clutch", "hype"], [], []);
        ClipSummaryResult untagged = tagged with { MoodTags = [] };

        Assert.Equal("A late win.\nMood: clutch, hype", tagged.EmbeddingText());
        Assert.Equal("A late win.", untagged.EmbeddingText());
    }

    [Fact]
    public void Quotes_RoundTripThroughJson()
    {
        ClipSummaryResult result = new("d", [], ["\"quoted\" line", "two"], []);

        Assert.Equal(result.Quotes, ClipSummaryResult.ParseQuotes(result.QuotesJson()));
    }

    [Fact]
    public void Message_NamesTheClipAndQuotesTheTranscript()
    {
        string message = Input(Transcript, playerLegend: "Wraith").ToMessage();

        Assert.Contains("Game: Apex Legends", message);
        Assert.Contains("Title: Final ring", message);
        Assert.Contains("Date: 14 March 2026", message);
        Assert.Contains("Legend the owner was playing (detected from the HUD): Wraith", message);
        Assert.Contains($"Transcript:\n\"\"\"\n{Transcript}\n\"\"\"", message.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Message_LeavesOutTheLegend_WhenNoneWasDetected()
    {
        Assert.DoesNotContain("Legend the owner", Input(Transcript, playerLegend: null).ToMessage());
    }

    [Fact]
    public void Message_SaysWhenThereIsNoSpeech()
    {
        Assert.Contains("Transcript: empty.", Input("  ").ToMessage());
        Assert.Contains("Transcript: none. The clip has no audio track", Input("", hasAudio: false).ToMessage());
        Assert.Contains("Transcript (only 2 words, so there is little speech to go on):", Input("oh no").ToMessage());
    }

    [Fact]
    public void Resources_AreEmbedded_AndVersionedByContent()
    {
        ClipSummaryResources resources = new();

        Assert.Contains("mood_tags", resources.Prompt);
        Assert.Matches("^[0-9a-f]{12}$", resources.PromptVersion);
        Assert.Equal(resources.PromptVersion, new ClipSummaryResources().PromptVersion);
    }

    [Fact]
    public void Schema_AllowsExactlyTheMoodTagList()
    {
        JsonElement tags = new ClipSummaryResources().ResponseSchema
            .GetProperty("properties").GetProperty("mood_tags").GetProperty("items").GetProperty("enum");

        Assert.Equal(ClipMoodTags.All, tags.EnumerateArray().Select(tag => tag.GetString()!).ToList());
    }

    [Fact]
    public void Prompt_DescribesEveryMoodTag()
    {
        string prompt = new ClipSummaryResources().Prompt;

        Assert.All(ClipMoodTags.All, tag => Assert.Contains($"`{tag}`", prompt));
    }

    [Fact]
    public async Task Summarizer_SendsThePromptAndClip_AndValidatesTheAnswer()
    {
        ClipSummaryResources resources = new();
        RecordingChatClient chatClient = new(Response(moodTags: ["hype", "made-up"], quotes: ["NO WAY", "nope"]));
        ChatClientClipSummarizer summarizer = new(chatClient, resources);

        ClipSummarization summarization = await summarizer.SummarizeAsync(Input(Transcript), CancellationToken.None);

        Assert.Equal(resources.Prompt, chatClient.Messages![0].Text);
        Assert.Equal(Input(Transcript).ToMessage(), chatClient.Messages[1].Text);
        ChatResponseFormatJson format = Assert.IsType<ChatResponseFormatJson>(chatClient.Options!.ResponseFormat);
        Assert.Equal(resources.ResponseSchema.GetRawText(), format.Schema!.Value.GetRawText());

        Assert.Equal(["hype"], summarization.Result.MoodTags);
        Assert.Equal(["NO WAY"], summarization.Result.Quotes);
        Assert.Equal(1200, summarization.InputTokens);
        Assert.Equal(900, summarization.CachedInputTokens);
        Assert.Equal(150, summarization.OutputTokens);
    }

    [Fact]
    public async Task Summarizer_KeepsTheRawResponse_WhenItCannotBeParsed()
    {
        ChatClientClipSummarizer summarizer = new(new RecordingChatClient("oops"), new ClipSummaryResources());

        JsonException ex = await Assert.ThrowsAnyAsync<JsonException>(() =>
            summarizer.SummarizeAsync(Input(Transcript), CancellationToken.None));

        Assert.Equal("oops", ex.Data["RawResponse"]);
    }

    [Theory]
    [InlineData("auto", true, "auto")]
    [InlineData("auto", false, "auto")]
    [InlineData("retry", true, "retry")]
    [InlineData("retry", false, null)]
    [InlineData("manual", true, null)]
    public void TranscriptionTrigger_DecidesTheSummaryTrigger(string transcription, bool heardSpeech, string? expected)
    {
        Assert.Equal(expected, ClipSummaryService.TriggerForTranscription(transcription, heardSpeech));
    }

    private static ClipSummaryInput Input(string transcript, string? playerLegend = null, bool? hasAudio = true)
    {
        return new ClipSummaryInput("Apex Legends", "Final ring", new DateTimeOffset(2026, 3, 14, 20, 0, 0, TimeSpan.Zero),
            playerLegend, hasAudio, transcript);
    }

    private static string Response(
        string description = "A squad wins the final fight.",
        string[]? moodTags = null,
        string[]? quotes = null,
        string[]? people = null)
    {
        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["description"] = description,
            ["mood_tags"] = moodTags ?? [],
            ["quotes"] = quotes ?? [],
            ["people"] = people ?? []
        });
    }

    private sealed class RecordingChatClient(string responseText) : IChatClient
    {
        public List<ChatMessage>? Messages { get; private set; }
        public ChatOptions? Options { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Messages = messages.ToList();
            Options = options;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText))
            {
                Usage = new UsageDetails { InputTokenCount = 1200, CachedInputTokenCount = 900, OutputTokenCount = 150 }
            });
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            return null;
        }

        public void Dispose()
        {
        }
    }
}
