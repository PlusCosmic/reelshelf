using System.Text.Json;
using Microsoft.Extensions.AI;
using Reelshelf.ClipSearch;
using Xunit;

namespace Reelshelf.Test.ClipSearch;

/// <summary>Reading a search into filters and a phrase, against a fake model.</summary>
public class ClipQueryParserTests
{
    private static readonly ClipQueryGame CallOfDuty = new(Guid.NewGuid(), "Call of Duty");
    private static readonly ClipQueryGame Apex = new(Guid.NewGuid(), "Apex Legends");
    private static readonly IReadOnlyList<ClipQueryGame> Games = [CallOfDuty, Apex];

    [Fact]
    public async Task ParseAsync_SendsTodayTheGamesAndTheSearch_WithTheSchema()
    {
        RecordingChatClient chat = new($$"""
            {"start_date":"2025-01-01","end_date":"2025-12-31","game_ids":["{{CallOfDuty.Id}}"],
             "mood_tags":["funny"],"people":[],"phrase":null}
            """);
        ClipQueryParserResources resources = new();
        ChatClientClipQueryParser parser = new(chat, resources);

        ClipQueryFilters filters = await parser.ParseAsync(
            new ClipQueryInput("  clips from 2025 playing cod where something funny happened ", new DateOnly(2026, 11, 11),
                Games),
            CancellationToken.None);

        Assert.Equal(new DateOnly(2025, 1, 1), filters.StartDate);
        Assert.Equal(new DateOnly(2025, 12, 31), filters.EndDate);
        Assert.Equal([CallOfDuty.Id], filters.GameCategoryIds);
        Assert.Equal(["funny"], filters.MoodTags);
        Assert.Empty(filters.People);
        Assert.Null(filters.Phrase);

        Assert.Equal(ChatRole.System, chat.Messages![0].Role);
        Assert.Equal(resources.Prompt, chat.Messages[0].Text);
        string message = chat.Messages[1].Text;
        Assert.Contains("Today: Wednesday 11 November 2026 (2026-11-11)", message);
        Assert.Contains($"- {CallOfDuty.Id}: Call of Duty", message);
        Assert.Contains($"- {Apex.Id}: Apex Legends", message);
        Assert.Contains("\"\"\"\nclips from 2025 playing cod where something funny happened\n\"\"\"", message.ReplaceLineEndings("\n"));
        ChatResponseFormatJson format = Assert.IsType<ChatResponseFormatJson>(chat.Options!.ResponseFormat);
        Assert.Equal("clip_search", format.SchemaName);
    }

    [Fact]
    public void Parse_KeepsOnlyTheSearchersGames_KnownTags_AndRealDays()
    {
        Guid someoneElsesGame = Guid.NewGuid();
        ClipQueryFilters filters = ClipQueryFilters.Parse($$"""
            {"start_date":"2026-10-31","end_date":"2026-10-01",
             "game_ids":["{{Apex.Id}}","{{someoneElsesGame}}","not a guid","{{Apex.Id}}"],
             "mood_tags":["Clutch","sad","funny"],"people":[" Sam ","sam","", "Jake"],"phrase":"  falling off the map "}
            """, Games);

        // Backwards dates are swapped rather than matching nothing.
        Assert.Equal(new DateOnly(2026, 10, 1), filters.StartDate);
        Assert.Equal(new DateOnly(2026, 10, 31), filters.EndDate);
        Assert.Equal([Apex.Id], filters.GameCategoryIds);
        // In the fixed list's order, whatever order the model gave.
        Assert.Equal(["funny", "clutch"], filters.MoodTags);
        Assert.Equal(["Sam", "Jake"], filters.People);
        Assert.Equal("falling off the map", filters.Phrase);
    }

    [Fact]
    public void Parse_DropsDatesThatArentDays_AndAnEmptyPhrase()
    {
        ClipQueryFilters filters = ClipQueryFilters.Parse("""
            {"start_date":"last month","end_date":"2026-02-30","game_ids":[],"mood_tags":[],"people":[],"phrase":"  "}
            """, Games);

        Assert.Null(filters.StartDate);
        Assert.Null(filters.EndDate);
        Assert.Empty(filters.GameCategoryIds);
        Assert.Null(filters.Phrase);
    }

    [Fact]
    public void Parse_ThrowsOnAnAnswerThatIsntTheSchema()
    {
        Assert.ThrowsAny<JsonException>(() => ClipQueryFilters.Parse("not json", Games));
        Assert.ThrowsAny<JsonException>(() => ClipQueryFilters.Parse("null", Games));
    }

    [Fact]
    public void Clean_CapsThePhraseAndPeople()
    {
        ClipQueryFilters filters = ClipQueryFilters.Clean(null, null, [Guid.Empty, Apex.Id, Apex.Id], null,
            Enumerable.Range(0, 10).Select(i => $"Friend {i}"), new string('a', 500));

        Assert.Equal([Apex.Id], filters.GameCategoryIds);
        Assert.Equal(ClipQueryFilters.MaxPeople, filters.People.Count);
        Assert.Equal(ClipQueryFilters.MaxPhraseLength, filters.Phrase!.Length);
        Assert.Empty(filters.MoodTags);
    }

    [Fact]
    public void TheSchemasMoodTags_AreTheSummariesFixedList()
    {
        ClipQueryParserResources resources = new();
        List<string> tags = resources.ResponseSchema.GetProperty("properties").GetProperty("mood_tags")
            .GetProperty("items").GetProperty("enum").EnumerateArray().Select(tag => tag.GetString()!).ToList();

        Assert.Equal(Reelshelf.ClipSummary.ClipMoodTags.All, tags);
        foreach (string tag in tags)
        {
            Assert.Contains($"`{tag}`", resources.Prompt);
        }
    }

    [Fact]
    public void Days_StartAtMidnightWhereTheSearcherIs()
    {
        TimeZoneInfo london = ClipSearchService.FindTimeZone("Europe/London");
        Assert.Equal(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(1)),
            ClipSearchService.StartOfDay(new DateOnly(2026, 7, 1), london));
        Assert.Equal(new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero),
            ClipSearchService.StartOfDay(new DateOnly(2026, 12, 1), london));
        Assert.Null(ClipSearchService.StartOfDay(null, london));
        Assert.Same(TimeZoneInfo.Utc, ClipSearchService.FindTimeZone("Not/AZone"));
        Assert.Same(TimeZoneInfo.Utc, ClipSearchService.FindTimeZone(null));
    }

    [Theory]
    [InlineData("none", ReasoningEffort.None)]
    [InlineData("minimal", ReasoningEffort.None)]
    [InlineData("low", ReasoningEffort.Low)]
    [InlineData(" Medium ", ReasoningEffort.Medium)]
    [InlineData("high", ReasoningEffort.High)]
    [InlineData("xhigh", ReasoningEffort.ExtraHigh)]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("loud", null)]
    public void ReasoningEffort_MapsTheSettingsSpelling(string? setting, ReasoningEffort? expected)
    {
        Assert.Equal(expected, ClipQueryParserFactory.ToReasoningEffort(setting));
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
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText)));
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
