using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Reelshelf.ApexLegends.LegendDetection;
using Xunit;

namespace Reelshelf.Test.ApexLegends;

/// <summary>
/// Runs the Decisions API recognizer against canned replies, pinning the request it sends (prompt, every image,
/// both questions with every legend as a choice) and how answers become a result.
/// </summary>
public class DecisionsRequestTests
{
    private static string Response(
        double hudProbability,
        string choice,
        double choiceProbability,
        string usage = "")
    {
        return $$"""
            {
              "answers": [
                { "type": "predicate", "name": "hud_detected", "probability": {{hudProbability}} },
                {
                  "type": "choice",
                  "name": "legend",
                  "choice": "{{choice}}",
                  "probabilities": [
                    { "value": "{{choice}}", "probability": {{choiceProbability}} },
                    { "value": "unidentifiable", "probability": 0.01 }
                  ],
                  "confidence": 0.5
                }
              ]{{usage}}
            }
            """;
    }

    [Fact]
    public async Task SendsPromptImagesAndQuestions()
    {
        RecordingHandler handler = new(Response(0.97, "Horizon", 0.93));
        LegendDetectionResources resources = new();

        LegendRecognition recognition = await Recognizer(handler, resources).RecognizeAsync(
            Images(new LegendFrame([1, 2, 3], "image/jpeg"), new LegendFrame([4, 5, 6], "image/jpeg")),
            CancellationToken.None);

        Assert.Equal("https://api.openai.com/v1/decisions", handler.RequestUri!.ToString());
        Assert.Equal("Bearer test-key", handler.Authorization);
        JsonElement request = handler.RequestBody!.Value;
        Assert.Equal("gpt-6-luna", request.GetProperty("model").GetString());

        List<JsonElement> parts = request.GetProperty("input")[0].GetProperty("content").EnumerateArray().ToList();
        Assert.Equal(resources.DecisionsPrompt, parts[0].GetProperty("text").GetString());
        List<string> images = parts
            .Where(part => part.GetProperty("type").GetString() == "input_image")
            .Select(part => part.GetProperty("image_url").GetString()!)
            .ToList();
        Assert.Equal(5, images.Count);
        Assert.StartsWith("data:image/png;base64,", images[0]);
        Assert.Equal("data:image/jpeg;base64," + Convert.ToBase64String([4, 5, 6]), images[4]);
        Assert.Contains(parts, part => part.TryGetProperty("text", out JsonElement text)
                                       && text.GetString() == "Screenshot 2 of 2, close-up of the bottom-left HUD panel:");

        JsonElement[] questions = request.GetProperty("questions").EnumerateArray().ToArray();
        Assert.Equal("predicate", questions[0].GetProperty("type").GetString());
        Assert.Equal("hud_detected", questions[0].GetProperty("name").GetString());
        Assert.Equal("choice", questions[1].GetProperty("type").GetString());
        List<string?> choices = questions[1].GetProperty("choices").EnumerateArray()
            .Select(choice => choice.GetProperty("value").GetString())
            .ToList();
        Assert.Equal([.. ApexLegendNames.All, "unidentifiable"], choices);

        Assert.True(recognition.Result.HudDetected);
        Assert.Equal("Horizon", recognition.Result.Player.Legend);
        Assert.Equal(0.93, recognition.Result.Player.LegendConfidence);
        Assert.Null(recognition.InputTokens);
        Assert.Contains("\"answers\"", recognition.RawResponse);
    }

    [Fact]
    public void Unidentifiable_IsNoLegend()
    {
        LegendRecognition recognition = DecisionsLegendRecognizer.Read(Response(0.2, "unidentifiable", 0.88));

        Assert.False(recognition.Result.HudDetected);
        Assert.Null(recognition.Result.Player.Legend);
        Assert.Equal(0, recognition.Result.Player.LegendConfidence);
    }

    [Fact]
    public void ChoiceOffTheSheet_IsDropped()
    {
        LegendRecognition recognition = DecisionsLegendRecognizer.Read(Response(0.9, "Kraber", 0.9));

        Assert.Null(recognition.Result.Player.Legend);
    }

    [Fact]
    public void ReadsUsage_WhenPresent()
    {
        LegendRecognition recognition = DecisionsLegendRecognizer.Read(Response(0.9, "Mad Maggie", 0.9,
            """, "usage": { "input_tokens": 12000, "input_tokens_details": { "cached_tokens": 9000 } }"""));

        Assert.Equal("Mad Maggie", recognition.Result.Player.Legend);
        Assert.Equal(12000, recognition.InputTokens);
        Assert.Equal(9000, recognition.CachedInputTokens);
        Assert.Null(recognition.OutputTokens);
    }

    [Fact]
    public async Task Refusal_FailsWithTheRawResponse()
    {
        const string refusal = """
            { "answers": [
                { "type": "predicate", "name": "hud_detected", "probability": 0.9 },
                { "type": "refusal", "name": "legend" }
            ] }
            """;
        RecordingHandler handler = new(refusal);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Recognizer(handler, new LegendDetectionResources())
                .RecognizeAsync(Images(new LegendFrame([1], "image/jpeg")), CancellationToken.None));

        Assert.Contains("refused legend", ex.Message);
        Assert.Equal(refusal, ex.Data["RawResponse"]);
    }

    [Fact]
    public async Task ErrorStatus_Throws()
    {
        RecordingHandler handler = new("""{ "error": { "message": "too many images" } }""", HttpStatusCode.BadRequest);

        HttpRequestException ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            Recognizer(handler, new LegendDetectionResources())
                .RecognizeAsync(Images(new LegendFrame([1], "image/jpeg")), CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Contains("too many images", ex.Message);
    }

    [Fact]
    public void PromptVersion_DiffersFromTheChatPrompt()
    {
        LegendDetectionResources resources = new();
        DecisionsLegendRecognizer recognizer = Recognizer(new RecordingHandler("{}"), resources);

        Assert.Equal(resources.DecisionsPromptVersion, recognizer.PromptVersion);
        Assert.NotEqual(resources.PromptVersion, recognizer.PromptVersion);
        Assert.Equal(12, recognizer.PromptVersion.Length);
    }

    [Fact]
    public void Factory_UsesTheOpenAIKey_ForDecisions()
    {
        LegendRecognizerFactory factory = Factory(new LegendDetectionOptions
        {
            Providers = { ["openai"] = new LegendDetectionProviderOptions { ApiKey = "key" } }
        });

        Assert.Equal(["openai", "openai-decisions"], factory.ConfiguredProviders());
        Assert.IsType<DecisionsLegendRecognizer>(factory.Create("OpenAI-Decisions", "gpt-6-luna", null));
        Assert.IsType<ChatClientLegendRecognizer>(factory.Create("openai", "gpt-6-luna", "low"));
        Assert.False(factory.SupportsReasoningEffort("openai-decisions"));
        Assert.True(factory.SupportsReasoningEffort("openai"));
        Assert.Equal("gpt-6-luna", factory.DefaultModel("openai-decisions"));
        Assert.Null(factory.DefaultModel("openai"));
    }

    [Fact]
    public void Factory_WithoutAnOpenAIKey_ConfiguresNothing()
    {
        Assert.Empty(Factory(new LegendDetectionOptions()).ConfiguredProviders());
    }

    private static LegendRecognizerFactory Factory(LegendDetectionOptions options)
    {
        return new LegendRecognizerFactory(new FixedOptions(options), new LegendDetectionResources(),
            new NewHttpClients());
    }

    private sealed class FixedOptions(LegendDetectionOptions value) : IOptionsMonitor<LegendDetectionOptions>
    {
        public LegendDetectionOptions CurrentValue => value;

        public LegendDetectionOptions Get(string? name)
        {
            return value;
        }

        public IDisposable? OnChange(Action<LegendDetectionOptions, string?> listener)
        {
            return null;
        }
    }

    private sealed class NewHttpClients : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient();
        }
    }

    private static DecisionsLegendRecognizer Recognizer(RecordingHandler handler, LegendDetectionResources resources)
    {
        return new DecisionsLegendRecognizer(new HttpClient(handler), "test-key", "gpt-6-luna", resources);
    }

    private static List<LegendScreenshot> Images(params LegendFrame[] panels)
    {
        return panels.Select(panel => new LegendScreenshot(new LegendFrame([0], "image/jpeg"), panel)).ToList();
    }

    private sealed class RecordingHandler(string responseJson, HttpStatusCode status = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? Authorization { get; private set; }
        public JsonElement? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization?.ToString();
            string body = await request.Content!.ReadAsStringAsync(cancellationToken);
            RequestBody = JsonDocument.Parse(body).RootElement.Clone();
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }
    }
}
