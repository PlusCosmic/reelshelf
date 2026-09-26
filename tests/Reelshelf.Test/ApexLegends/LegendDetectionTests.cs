using System.Text.Json;
using Microsoft.Extensions.AI;
using Reelshelf.ApexLegends.LegendDetection;
using Xunit;

namespace Reelshelf.Test.ApexLegends;

public class LegendDetectionTests
{
    private const string ValidResponse = """
        {
          "hud_detected": true,
          "player": { "name": "PlusCosmic", "legend": "mad_maggie", "name_confidence": 0.95, "legend_confidence": 0.92 },
          "teammates": [
            { "slot": 1, "name": "[TAG] Mate", "legend": "Wraith", "name_confidence": 0.8, "legend_confidence": 0.9 },
            { "slot": 2, "name": null, "legend": "Not A Legend", "name_confidence": 0, "legend_confidence": 0.7 }
          ]
        }
        """;

    [Fact]
    public void Parse_KeepsLegendsOnTheSheet_AndDropsInventedOnes()
    {
        LegendDetectionResult result = LegendDetectionResult.Parse(ValidResponse);

        Assert.True(result.HudDetected);
        Assert.Equal("PlusCosmic", result.Player.Name);
        Assert.Equal("Mad Maggie", result.Player.Legend);
        Assert.Equal(0.92, result.Player.LegendConfidence);
        Assert.Equal("[TAG] Mate", result.Teammates[0].Name);
        Assert.Equal("Wraith", result.Teammates[0].Legend);
        Assert.Null(result.Teammates[1].Legend);
        Assert.Equal(0, result.Teammates[1].LegendConfidence);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "hud_detected": false }""")]
    public void Parse_RejectsResponsesThatDoNotMatchTheSchema(string json)
    {
        Assert.ThrowsAny<JsonException>(() => LegendDetectionResult.Parse(json));
    }

    [Fact]
    public void Teammates_RoundTripThroughStorage()
    {
        LegendDetectionResult result = LegendDetectionResult.Parse(ValidResponse);

        List<DetectedTeammate> stored = LegendDetectionResult.ParseTeammates(result.TeammatesJson());

        Assert.Equal(result.Teammates, stored);
    }

    [Theory]
    [InlineData("wraith", "Wraith")]
    [InlineData("MAD MAGGIE", "Mad Maggie")]
    [InlineData("Mad-Maggie", "Mad Maggie")]
    [InlineData("Axle", "Axle")]
    [InlineData("Revenant Reborn", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Canonicalize_UsesTheSheetSpelling(string? input, string? expected)
    {
        Assert.Equal(expected, ApexLegendNames.Canonicalize(input));
    }

    [Fact]
    public void ThumbnailUrls_AreTheSixBunnyThumbnails()
    {
        Guid videoId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        List<string> urls = LegendDetectionService.ThumbnailUrls("https://cdn.example/", videoId);

        Assert.Equal(
        [
            $"https://cdn.example/{videoId}/thumbnail.jpg",
            $"https://cdn.example/{videoId}/thumbnail_1.jpg",
            $"https://cdn.example/{videoId}/thumbnail_2.jpg",
            $"https://cdn.example/{videoId}/thumbnail_3.jpg",
            $"https://cdn.example/{videoId}/thumbnail_4.jpg",
            $"https://cdn.example/{videoId}/thumbnail_5.jpg"
        ], urls);
    }

    [Fact]
    public void Resources_AreEmbedded_AndVersionedByContent()
    {
        LegendDetectionResources resources = new();

        Assert.False(string.IsNullOrWhiteSpace(resources.Prompt));
        Assert.Equal(JsonValueKind.Object, resources.ResponseSchema.ValueKind);
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], resources.ReferenceSheet[..4]);
        Assert.Matches("^[0-9a-f]{12}$", resources.PromptVersion);
        Assert.Equal(resources.PromptVersion, new LegendDetectionResources().PromptVersion);
    }

    [Fact]
    public async Task Recognizer_SendsPromptThenReferenceSheetThenFrames_WithTheSchema()
    {
        LegendDetectionResources resources = new();
        RecordingChatClient chatClient = new(ValidResponse);
        ChatClientLegendRecognizer recognizer = new(chatClient, resources);
        LegendFrame[] frames = [new([1, 2, 3], "image/jpeg"), new([4, 5, 6], "image/jpeg")];

        LegendRecognition recognition = await recognizer.RecognizeAsync(frames, CancellationToken.None);

        Assert.Equal("Mad Maggie", recognition.Result.Player.Legend);
        Assert.Equal(ValidResponse, recognition.RawResponse);
        Assert.Equal(1200, recognition.InputTokens);
        Assert.Equal(900, recognition.CachedInputTokens);

        List<ChatMessage> messages = chatClient.Messages!;
        Assert.Equal(ChatRole.System, messages[0].Role);
        Assert.Equal(resources.Prompt, messages[0].Text);

        List<DataContent> images = messages[1].Contents.OfType<DataContent>().ToList();
        Assert.Equal(3, images.Count);
        Assert.Equal("image/png", images[0].MediaType);
        Assert.Equal(resources.ReferenceSheet, images[0].Data.ToArray());
        Assert.Equal(new byte[] { 4, 5, 6 }, images[2].Data.ToArray());
        Assert.Contains(messages[1].Contents.OfType<TextContent>(), text => text.Text == "Screenshot 2 of 2:");

        ChatResponseFormatJson format = Assert.IsType<ChatResponseFormatJson>(chatClient.Options!.ResponseFormat);
        Assert.Equal(resources.ResponseSchema.GetRawText(), format.Schema!.Value.GetRawText());
    }

    [Fact]
    public async Task Recognizer_KeepsTheRawResponse_WhenItCannotBeParsed()
    {
        ChatClientLegendRecognizer recognizer = new(new RecordingChatClient("oops"), new LegendDetectionResources());

        JsonException ex = await Assert.ThrowsAnyAsync<JsonException>(() =>
            recognizer.RecognizeAsync([new LegendFrame([1], "image/jpeg")], CancellationToken.None));

        Assert.Equal("oops", ex.Data["RawResponse"]);
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
