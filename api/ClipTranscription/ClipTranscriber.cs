using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Reelshelf.ClipTranscription;

/// <summary>The text a model heard in a clip's audio, with the usage it reported.</summary>
public sealed record ClipTranscript(
    string Text,
    IReadOnlyList<string> Languages,
    int? InputTokens,
    int? OutputTokens,
    string RawResponse);

public interface IClipTranscriber
{
    Task<ClipTranscript> TranscribeAsync(
        string audioPath,
        string model,
        TranscriptionPrompt prompt,
        CancellationToken cancellationToken);
}

/// <summary>
/// A transcription request the provider refused. <see cref="Retryable"/> is false for requests that will fail
/// the same way again (bad input, bad key), so the run is not retried.
/// </summary>
public sealed class ClipTranscriptionException(string message, bool retryable) : Exception(message)
{
    public bool Retryable { get; } = retryable;
}

/// <summary>
/// Calls OpenAI's <c>audio/transcriptions</c> endpoint directly: the SDK's typed options don't carry
/// <c>chunking_strategy</c> or <c>gpt-transcribe</c>'s <c>keywords</c>, and the multipart form is small enough
/// to own.
/// </summary>
/// <remarks>
/// Every request asks for <c>chunking_strategy=auto</c>, which normalises loudness and splits the audio on voice
/// activity first. Without it, both <c>gpt-transcribe</c> and <c>gpt-4o-transcribe</c> often returned no text at
/// all for clips where voices sit under loud game audio, and whether they did varied from one call to the next.
/// </remarks>
public sealed class OpenAIClipTranscriber(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<ClipTranscriptionOptions> options) : IClipTranscriber
{
    public const string HttpClientName = "clip-transcription";

    /// <summary>Bumped when the request's shape changes, so runs made with an older shape keep their own version.</summary>
    public const string RequestLayoutVersion = "chunking-auto";

    /// <summary>Only the <c>gpt-transcribe</c> family accepts <c>keywords</c>.</summary>
    public static bool SupportsKeywords(string model)
    {
        return model.StartsWith("gpt-transcribe", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<ClipTranscript> TranscribeAsync(
        string audioPath,
        string model,
        TranscriptionPrompt prompt,
        CancellationToken cancellationToken)
    {
        ClipTranscriptionOptions current = options.CurrentValue;
        HttpClient client = httpClientFactory.CreateClient(HttpClientName);

        await using FileStream audio = File.OpenRead(audioPath);
        using HttpRequestMessage request = new(HttpMethod.Post, new Uri(new Uri(current.BaseUrl), "audio/transcriptions"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", current.ApiKey);
        request.Content = BuildForm(audio, Path.GetFileName(audioPath), model, prompt);

        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            bool retryable = response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout
                             || (int)response.StatusCode >= 500;
            throw new ClipTranscriptionException(
                $"Transcription returned {(int)response.StatusCode}: {ErrorMessage(body)}", retryable);
        }

        return Parse(body);
    }

    internal static MultipartFormDataContent BuildForm(
        Stream audio,
        string fileName,
        string model,
        TranscriptionPrompt prompt)
    {
        StreamContent file = new(audio);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/mp4");

        MultipartFormDataContent form = new()
        {
            { file, "file", fileName },
            { new StringContent(model), "model" },
            { new StringContent("json"), "response_format" },
            { new StringContent(prompt.Prompt), "prompt" },
            { new StringContent("auto"), "chunking_strategy" }
        };
        foreach (string keyword in prompt.Keywords)
        {
            form.Add(new StringContent(keyword), "keywords[]");
        }

        return form;
    }

    internal static ClipTranscript Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        string text = root.TryGetProperty("text", out JsonElement textElement) ? textElement.GetString() ?? "" : "";
        List<string> languages = [];
        if (root.TryGetProperty("languages", out JsonElement languagesElement)
            && languagesElement.ValueKind == JsonValueKind.Array)
        {
            languages.AddRange(languagesElement.EnumerateArray()
                .Select(language => language.ValueKind == JsonValueKind.Object
                                    && language.TryGetProperty("code", out JsonElement code)
                    ? code.GetString()
                    : null)
                .OfType<string>());
        }

        int? inputTokens = null;
        int? outputTokens = null;
        if (root.TryGetProperty("usage", out JsonElement usage) && usage.ValueKind == JsonValueKind.Object)
        {
            inputTokens = ReadInt(usage, "input_tokens");
            outputTokens = ReadInt(usage, "output_tokens");
        }

        return new ClipTranscript(text.Trim(), languages, inputTokens, outputTokens, json);
    }

    private static int? ReadInt(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int number)
            ? number
            : null;
    }

    private static string ErrorMessage(string body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out JsonElement error)
                && error.TryGetProperty("message", out JsonElement message))
            {
                return message.GetString() ?? body;
            }
        }
        catch (JsonException)
        {
            // Not JSON; report the body as it came.
        }

        return body.Length > 500 ? body[..500] : body;
    }
}
