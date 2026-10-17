using System.Net;
using System.Text;
using System.Text.Json;
using Anthropic;
using Reelshelf.ApexLegends.LegendDetection;
using Xunit;

namespace Reelshelf.Test.ApexLegends;

/// <summary>
/// Runs the real Anthropic SDK against a canned Messages API reply, pinning the request legend detection sends (the
/// prompt, the sheet in halves, the cache breakpoint after it, the schema and effort) and how usage is read back.
/// Splitting the sheet runs the real ffmpeg, which CI installs.
/// </summary>
public class AnthropicRequestTests
{
    private const string AnswerJson =
        "{\\\"hud_detected\\\":true,\\\"player\\\":{\\\"legend\\\":\\\"Horizon\\\",\\\"legend_confidence\\\":0.97}}";

    private static string Response(string stopReason = "end_turn", string text = AnswerJson)
    {
        return $$"""
            {
              "id": "msg_1",
              "type": "message",
              "role": "assistant",
              "model": "claude-sonnet-5-5",
              "content": [
                { "type": "thinking", "thinking": "", "signature": "sig" },
                { "type": "text", "text": "{{text}}" }
              ],
              "stop_reason": "{{stopReason}}",
              "stop_sequence": null,
              "usage": {
                "input_tokens": 600,
                "cache_read_input_tokens": 3000,
                "cache_creation_input_tokens": 400,
                "output_tokens": 250
              }
            }
            """;
    }

    [Fact]
    public async Task SendsPromptSheetHalvesAndSchema_AndReadsUsage()
    {
        RecordingHandler handler = new(Response());
        LegendDetectionResources resources = new();

        LegendRecognition recognition = await Recognizer(handler, resources, "high").RecognizeAsync(
            Images(new LegendFrame([1, 2, 3], "image/jpeg"), new LegendFrame([4, 5, 6], "image/jpeg")),
            CancellationToken.None);

        Assert.Equal("https://api.anthropic.com/v1/messages", handler.RequestUri!.ToString());
        Assert.Equal("test-key", handler.ApiKey);
        JsonElement request = handler.RequestBody!.Value;
        Assert.Equal("claude-sonnet-5-5", request.GetProperty("model").GetString());
        Assert.Equal(AnthropicLegendRecognizer.MaxTokens, request.GetProperty("max_tokens").GetInt32());
        Assert.Equal(resources.Prompt, request.GetProperty("system").GetString());
        Assert.False(request.TryGetProperty("thinking", out _));

        JsonElement outputConfig = request.GetProperty("output_config");
        Assert.Equal("high", outputConfig.GetProperty("effort").GetString());
        Assert.Equal("json_schema", outputConfig.GetProperty("format").GetProperty("type").GetString());
        JsonElement schema = outputConfig.GetProperty("format").GetProperty("schema");
        Assert.Equal(["hud_detected", "player"],
            schema.GetProperty("required").EnumerateArray().Select(name => name.GetString()).ToList());
        Assert.DoesNotContain("minimum", schema.GetRawText());

        List<JsonElement> parts = request.GetProperty("messages")[0].GetProperty("content").EnumerateArray().ToList();
        List<JsonElement> images = parts.Where(part => part.GetProperty("type").GetString() == "image").ToList();
        Assert.Equal(6, images.Count);
        Assert.Equal("image/png", images[0].GetProperty("source").GetProperty("media_type").GetString());
        Assert.Equal("image/png", images[1].GetProperty("source").GetProperty("media_type").GetString());
        Assert.Equal(Convert.ToBase64String([4, 5, 6]), images[5].GetProperty("source").GetProperty("data").GetString());

        // One breakpoint, directly after the sheet: everything before it is identical for every clip.
        int breakpoint = parts.FindIndex(part => part.TryGetProperty("cache_control", out _));
        Assert.Equal(1, parts.Count(part => part.TryGetProperty("cache_control", out _)));
        Assert.Equal("ephemeral", parts[breakpoint].GetProperty("cache_control").GetProperty("type").GetString());
        Assert.Equal(2, parts.Take(breakpoint).Count(part => part.GetProperty("type").GetString() == "image"));

        Assert.True(recognition.Result.HudDetected);
        Assert.Equal("Horizon", recognition.Result.Player.Legend);
        Assert.Equal(0.97, recognition.Result.Player.LegendConfidence);
        Assert.Equal(4000, recognition.InputTokens);
        Assert.Equal(3000, recognition.CachedInputTokens);
        Assert.Equal(250, recognition.OutputTokens);
        Assert.Equal(400, recognition.CacheWriteInputTokens);
        Assert.Null(recognition.CostUsd);
    }

    [Fact]
    public async Task NoEffort_SendsNone()
    {
        RecordingHandler handler = new(Response());

        await Recognizer(handler, new LegendDetectionResources(), null)
            .RecognizeAsync(Images(new LegendFrame([1], "image/jpeg")), CancellationToken.None);

        Assert.False(handler.RequestBody!.Value.GetProperty("output_config").TryGetProperty("effort", out _));
    }

    [Fact]
    public async Task EffortWithoutAnAnthropicLevel_FailsBeforeSending()
    {
        RecordingHandler handler = new(Response());

        await Assert.ThrowsAsync<ArgumentException>(() => Recognizer(handler, new LegendDetectionResources(), "minimal")
            .RecognizeAsync(Images(new LegendFrame([1], "image/jpeg")), CancellationToken.None));

        Assert.Null(handler.RequestUri);
    }

    [Fact]
    public async Task Refusal_FailsTheRun()
    {
        RecordingHandler handler = new(Response("refusal", ""));

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Recognizer(handler, new LegendDetectionResources(), null)
                .RecognizeAsync(Images(new LegendFrame([1], "image/jpeg")), CancellationToken.None));

        Assert.Contains("refused", ex.Message);
    }

    [Fact]
    public async Task MalformedAnswer_KeepsTheRawResponse()
    {
        RecordingHandler handler = new(Response("max_tokens", "{\\\"hud_detected\\\":tr"));

        JsonException ex = await Assert.ThrowsAsync<JsonException>(() =>
            Recognizer(handler, new LegendDetectionResources(), null)
                .RecognizeAsync(Images(new LegendFrame([1], "image/jpeg")), CancellationToken.None));

        Assert.Equal("{\"hud_detected\":tr", ex.Data["RawResponse"]);
    }

    [Fact]
    public void PromptVersion_DiffersFromTheOpenAIRequest()
    {
        LegendDetectionResources resources = new();
        AnthropicLegendRecognizer recognizer = Recognizer(new RecordingHandler("{}"), resources, null);

        Assert.Equal(resources.AnthropicPromptVersion, recognizer.PromptVersion);
        Assert.NotEqual(resources.PromptVersion, recognizer.PromptVersion);
    }

    private static AnthropicLegendRecognizer Recognizer(
        RecordingHandler handler,
        LegendDetectionResources resources,
        string? effort)
    {
        AnthropicClient client = new() { ApiKey = "test-key", HttpClient = new HttpClient(handler), MaxRetries = 0 };
        return new AnthropicLegendRecognizer(client, "claude-sonnet-5-5", effort, resources);
    }

    private static List<LegendScreenshot> Images(params LegendFrame[] panels)
    {
        return panels.Select(panel => new LegendScreenshot(new LegendFrame([0], "image/jpeg"), panel)).ToList();
    }

    private sealed class RecordingHandler(string responseJson) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? ApiKey { get; private set; }
        public JsonElement? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            ApiKey = request.Headers.TryGetValues("x-api-key", out IEnumerable<string>? values) ? values.Single() : null;
            string body = await request.Content!.ReadAsStringAsync(cancellationToken);
            RequestBody = JsonDocument.Parse(body).RootElement.Clone();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }
    }
}
