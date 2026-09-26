using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using OpenAI;
using Reelshelf.ApexLegends.LegendDetection;
using Xunit;

namespace Reelshelf.Test.ApexLegends;

/// <summary>
/// Runs the real OpenAI adapter against a canned Responses API reply, pinning the request legend detection
/// sends (caching options, no storage, the schema, every image) and how usage is read back.
/// </summary>
public class OpenAIRequestTests
{
    private const string ResponseJson = """
        {
          "id": "resp_1",
          "object": "response",
          "created_at": 1790000000,
          "status": "completed",
          "model": "gpt-6-luna",
          "output": [
            {
              "type": "message",
              "id": "msg_1",
              "status": "completed",
              "role": "assistant",
              "content": [
                {
                  "type": "output_text",
                  "annotations": [],
                  "text": "{\"hud_detected\":true,\"player\":{\"name\":\"Cosmic\",\"legend\":\"Horizon\",\"name_confidence\":0.99,\"legend_confidence\":0.99},\"teammates\":[]}"
                }
              ]
            }
          ],
          "parallel_tool_calls": true,
          "tools": [],
          "usage": {
            "input_tokens": 17000,
            "input_tokens_details": { "cached_tokens": 15000 },
            "output_tokens": 200,
            "output_tokens_details": { "reasoning_tokens": 0 },
            "total_tokens": 17200
          }
        }
        """;

    [Fact]
    public async Task SendsCachingOptionsAndSchema_AndReadsCachedTokens()
    {
        RecordingHandler handler = new(ResponseJson);
        LegendDetectionResources resources = new();
        ChatClientLegendRecognizer recognizer = new(CreateClient(handler, "implicit"), resources);

        LegendRecognition recognition = await recognizer.RecognizeAsync(
            [new LegendFrame([1, 2, 3], "image/jpeg"), new LegendFrame([4, 5, 6], "image/jpeg")],
            CancellationToken.None);

        Assert.Equal("Horizon", recognition.Result.Player.Legend);
        Assert.Equal(17000, recognition.InputTokens);
        Assert.Equal(15000, recognition.CachedInputTokens);

        Assert.EndsWith("/responses", handler.RequestUri!.AbsolutePath);
        JsonElement request = handler.RequestBody!.Value;
        Assert.Equal("gpt-6-luna", request.GetProperty("model").GetString());
        Assert.Equal("implicit", request.GetProperty("prompt_cache_options").GetProperty("mode").GetString());
        Assert.Equal("reelshelf-legend-detection", request.GetProperty("prompt_cache_key").GetString());
        Assert.False(request.GetProperty("store").GetBoolean());

        JsonElement format = request.GetProperty("text").GetProperty("format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.Equal(
            resources.ResponseSchema.GetProperty("required").GetRawText(),
            format.GetProperty("schema").GetProperty("required").GetRawText());

        string body = request.GetRawText();
        Assert.Equal(3, CountOccurrences(body, "\"input_image\""));
        Assert.Contains("Screenshot 2 of 2:", body);
    }

    [Fact]
    public async Task EmptyCacheMode_SendsNoCachingOptions()
    {
        RecordingHandler handler = new(ResponseJson);
        IChatClient client = CreateClient(handler, "");

        await client.GetResponseAsync("hello");

        Assert.False(handler.RequestBody!.Value.TryGetProperty("prompt_cache_options", out _));
        Assert.Equal("reelshelf-legend-detection", handler.RequestBody!.Value.GetProperty("prompt_cache_key").GetString());
    }

    private static IChatClient CreateClient(RecordingHandler handler, string cacheMode)
    {
        return LegendRecognizerFactory.CreateOpenAIChatClient(
            "gpt-6-luna",
            new LegendDetectionProviderOptions { ApiKey = "test", PromptCacheMode = cacheMode },
            new OpenAIClientOptions { Transport = new HttpClientPipelineTransport(new HttpClient(handler)) });
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        for (int index = text.IndexOf(value, StringComparison.Ordinal);
             index >= 0;
             index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private sealed class RecordingHandler(string responseJson) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public JsonElement? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            string body = await request.Content!.ReadAsStringAsync(cancellationToken);
            RequestBody = JsonDocument.Parse(body).RootElement.Clone();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }
    }
}
