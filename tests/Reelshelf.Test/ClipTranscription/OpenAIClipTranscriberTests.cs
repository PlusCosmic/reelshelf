using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Reelshelf.ClipTranscription;
using Xunit;

namespace Reelshelf.Test.ClipTranscription;

/// <summary>
/// Runs the transcriber against a canned <c>audio/transcriptions</c> reply, pinning the multipart form sent to
/// <c>gpt-transcribe</c> and how the transcript, languages and usage are read back.
/// </summary>
public class OpenAIClipTranscriberTests
{
    private const string ResponseJson = """
        {
          "text": "  no way, he's one shot, push Wraith!  ",
          "languages": [{ "code": "en" }],
          "usage": {
            "type": "tokens",
            "input_tokens": 410,
            "output_tokens": 14,
            "total_tokens": 424,
            "input_token_details": { "audio_tokens": 380, "text_tokens": 30 }
          }
        }
        """;

    [Fact]
    public async Task SendsModelPromptAndEveryKeyword_AndReadsTheTranscript()
    {
        RecordingHandler handler = new(HttpStatusCode.OK, ResponseJson);
        string audioPath = await WriteAudioAsync();
        TranscriptionPrompt prompt = new("A gameplay clip from Apex Legends.", ["Apex Legends", "Wraith", "Mad Maggie"]);

        ClipTranscript transcript = await CreateTranscriber(handler)
            .TranscribeAsync(audioPath, "gpt-transcribe", prompt, CancellationToken.None);

        Assert.Equal("no way, he's one shot, push Wraith!", transcript.Text);
        Assert.Equal(["en"], transcript.Languages);
        Assert.Equal(410, transcript.InputTokens);
        Assert.Equal(14, transcript.OutputTokens);

        Assert.Equal("https://api.openai.com/v1/audio/transcriptions", handler.RequestUri!.ToString());
        Assert.Equal("Bearer test-key", handler.Authorization);
        Assert.Equal(["gpt-transcribe"], handler.Fields["model"]);
        Assert.Equal(["json"], handler.Fields["response_format"]);
        Assert.Equal(["auto"], handler.Fields["chunking_strategy"]);
        Assert.Equal(["A gameplay clip from Apex Legends."], handler.Fields["prompt"]);
        Assert.Equal(["Apex Legends", "Wraith", "Mad Maggie"], handler.Fields["keywords[]"]);
        Assert.Equal([Path.GetFileName(audioPath)], handler.FileNames);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    public async Task ErrorResponses_ReportTheMessage_AndWhetherToRetry(HttpStatusCode status, bool retryable)
    {
        RecordingHandler handler = new(status, """{ "error": { "message": "Audio file is too short" } }""");
        string audioPath = await WriteAudioAsync();

        ClipTranscriptionException ex = await Assert.ThrowsAsync<ClipTranscriptionException>(() =>
            CreateTranscriber(handler).TranscribeAsync(
                audioPath, "gpt-transcribe", new TranscriptionPrompt("p", []), CancellationToken.None));

        Assert.Equal(retryable, ex.Retryable);
        Assert.Contains("Audio file is too short", ex.Message);
        Assert.Contains(((int)status).ToString(), ex.Message);
    }

    [Fact]
    public void Parse_ReadsDurationUsage_WithoutTokens()
    {
        ClipTranscript transcript = OpenAIClipTranscriber.Parse(
            """{ "text": "", "languages": [], "usage": { "type": "duration", "seconds": 121 } }""");

        Assert.Equal("", transcript.Text);
        Assert.Null(transcript.InputTokens);
        Assert.Null(transcript.OutputTokens);
    }

    [Fact]
    public void Parse_ToleratesMissingLanguagesAndUsage()
    {
        ClipTranscript transcript = OpenAIClipTranscriber.Parse("""{ "text": "" }""");

        Assert.Equal("", transcript.Text);
        Assert.Empty(transcript.Languages);
        Assert.Null(transcript.InputTokens);
    }

    private static OpenAIClipTranscriber CreateTranscriber(RecordingHandler handler)
    {
        return new OpenAIClipTranscriber(
            new SingleClientFactory(new HttpClient(handler)),
            new StaticOptionsMonitor<ClipTranscriptionOptions>(new ClipTranscriptionOptions { ApiKey = "test-key" }));
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
        public Dictionary<string, List<string>> Fields { get; } = [];
        public List<string> FileNames { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization?.ToString();
            foreach (HttpContent part in (MultipartFormDataContent)request.Content!)
            {
                string name = part.Headers.ContentDisposition!.Name!.Trim('"');
                if (part.Headers.ContentDisposition.FileName is { } fileName)
                {
                    FileNames.Add(fileName.Trim('"'));
                    continue;
                }

                if (!Fields.TryGetValue(name, out List<string>? values))
                {
                    Fields[name] = values = [];
                }

                values.Add(await part.ReadAsStringAsync(cancellationToken));
            }

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
