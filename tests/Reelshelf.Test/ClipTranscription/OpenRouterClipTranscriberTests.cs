using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Reelshelf.ClipTranscription;
using Xunit;

namespace Reelshelf.Test.ClipTranscription;

/// <summary>
/// Runs the transcriber against canned OpenRouter <c>audio/transcriptions</c> replies, pinning what each model
/// family is sent under <c>provider.options</c> and how the transcript and usage are read back.
/// </summary>
public class OpenRouterClipTranscriberTests
{
    private const string ResponseJson = """
        {
          "text": "  no way, he's one shot, push Wraith!  ",
          "usage": { "total_tokens": 3014, "input_tokens": 3000, "output_tokens": 14, "cost": 0.006 }
        }
        """;

    [Fact]
    public async Task Gemini_GetsTheAudioAndVocabulary_AndNoPrompt()
    {
        RecordingHandler handler = new(HttpStatusCode.OK, ResponseJson);
        string audioPath = await WriteAudioAsync();
        TranscriptionPrompt prompt = TranscriptionPrompt.For("Apex Legends", "apex-legends", "google/gemini-3.5-transcribe");

        ClipTranscript transcript = await CreateTranscriber(handler)
            .TranscribeAsync(audioPath, "google/gemini-3.5-transcribe", prompt, CancellationToken.None);

        Assert.Equal("no way, he's one shot, push Wraith!", transcript.Text);
        Assert.Equal(3000, transcript.InputTokens);
        Assert.Equal(14, transcript.OutputTokens);

        Assert.Equal("https://openrouter.ai/api/v1/audio/transcriptions", handler.RequestUri!.ToString());
        Assert.Equal("Bearer test-key", handler.Authorization);
        JsonElement request = handler.Body!.Value;
        Assert.Equal("google/gemini-3.5-transcribe", request.GetProperty("model").GetString());
        Assert.Equal("m4a", request.GetProperty("input_audio").GetProperty("format").GetString());
        Assert.Equal([0, 1, 2, 3], Convert.FromBase64String(request.GetProperty("input_audio").GetProperty("data").GetString()!));

        JsonElement options = request.GetProperty("provider").GetProperty("options");
        List<string> vocabulary = options.GetProperty("google-ai-studio").GetProperty("custom_vocabulary")
            .EnumerateArray().Select(term => term.GetString()!).ToList();
        Assert.Equal("Apex Legends", vocabulary[0]);
        Assert.Contains("Mad Maggie", vocabulary);
        Assert.Null(prompt.Prompt);
    }

    [Fact]
    public void OpenAI_GetsChunkingAndThePrompt_AndKeywordsOnlyForGptTranscribe()
    {
        JsonElement gptTranscribe = Options("openai/gpt-transcribe").GetProperty("openai");
        Assert.Equal("auto", gptTranscribe.GetProperty("chunking_strategy").GetString());
        Assert.Contains("Apex Legends", gptTranscribe.GetProperty("prompt").GetString());
        Assert.Equal("Apex Legends", gptTranscribe.GetProperty("keywords")[0].GetString());

        JsonElement gpt4o = Options("openai/gpt-4o-transcribe").GetProperty("openai");
        Assert.Equal("auto", gpt4o.GetProperty("chunking_strategy").GetString());
        Assert.False(gpt4o.TryGetProperty("keywords", out _));
    }

    [Fact]
    public void Deepgram_GetsOnlyFormatting()
    {
        JsonElement deepgram = Options("deepgram/nova-3").GetProperty("deepgram");
        Assert.True(deepgram.GetProperty("smart_format").GetBoolean());
        Assert.Single(deepgram.EnumerateObject());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData((HttpStatusCode)520, true)]
    public async Task ErrorResponses_ReportTheMessage_AndWhetherToRetry(HttpStatusCode status, bool retryable)
    {
        RecordingHandler handler = new(status, """{ "error": { "message": "Provider returned an error", "code": 1 } }""");
        string audioPath = await WriteAudioAsync();

        ClipTranscriptionException ex = await Assert.ThrowsAsync<ClipTranscriptionException>(() =>
            CreateTranscriber(handler).TranscribeAsync(
                audioPath, "deepgram/nova-3", new TranscriptionPrompt(null, []), CancellationToken.None));

        Assert.Equal(retryable, ex.Retryable);
        Assert.Contains("Provider returned an error", ex.Message);
        Assert.Contains(((int)status).ToString(), ex.Message);
    }

    [Fact]
    public void Parse_ToleratesMissingUsage_AndReadsALanguage()
    {
        ClipTranscript transcript = OpenRouterClipTranscriber.Parse("""{ "text": "", "language": "en" }""");

        Assert.Equal("", transcript.Text);
        Assert.Equal(["en"], transcript.Languages);
        Assert.Null(transcript.InputTokens);
    }

    private static JsonElement Options(string model)
    {
        TranscriptionPrompt prompt = TranscriptionPrompt.For("Apex Legends", "apex-legends", model);
        string json = OpenRouterClipTranscriber.BuildRequest([1], model, prompt).ToJsonString();
        return JsonDocument.Parse(json).RootElement.GetProperty("provider").GetProperty("options").Clone();
    }

    private static OpenRouterClipTranscriber CreateTranscriber(RecordingHandler handler)
    {
        return new OpenRouterClipTranscriber(
            new SingleClientFactory(new HttpClient(handler)),
            new StaticOptionsMonitor<ClipTranscriptionOptions>(new ClipTranscriptionOptions { OpenRouterApiKey = "test-key" }));
    }

    private static async Task<string> WriteAudioAsync()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.m4a");
        await File.WriteAllBytesAsync(path, [0, 1, 2, 3]);
        return path;
    }

    private sealed class RecordingHandler(HttpStatusCode status, string responseJson) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? Authorization { get; private set; }
        public JsonElement? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization?.ToString();
            Body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement.Clone();
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
