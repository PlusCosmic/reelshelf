using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
/// Transcribes through OpenRouter's <c>audio/transcriptions</c> endpoint, so the model id alone (for example
/// <c>google/gemini-3.5-transcribe</c> or <c>deepgram/nova-3</c>) picks the provider. Settings a provider only
/// exposes itself (vocabulary, prompt, chunking) go under <c>provider.options</c>, keyed by the provider's tag and
/// using its own field names; see <see cref="ProviderOptions"/>.
/// </summary>
public sealed class OpenRouterClipTranscriber(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<ClipTranscriptionOptions> options) : IClipTranscriber
{
    public const string HttpClientName = "clip-transcription";

    /// <summary>Bumped when the request's shape changes, so runs made with an older shape keep their own version.</summary>
    public const string RequestLayoutVersion = "openrouter-v1";

    public async Task<ClipTranscript> TranscribeAsync(
        string audioPath,
        string model,
        TranscriptionPrompt prompt,
        CancellationToken cancellationToken)
    {
        ClipTranscriptionOptions current = options.CurrentValue;
        HttpClient client = httpClientFactory.CreateClient(HttpClientName);

        byte[] audio = await File.ReadAllBytesAsync(audioPath, cancellationToken);
        using HttpRequestMessage request = new(HttpMethod.Post, new Uri(new Uri(current.BaseUrl), "audio/transcriptions"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", current.OpenRouterApiKey);
        request.Content = new StringContent(BuildRequest(audio, model, prompt).ToJsonString(), Encoding.UTF8,
            "application/json");

        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // OpenRouter passes provider outages through as 5xx (including Cloudflare-style 520s); a 413 or 400
            // will fail the same way again.
            bool retryable = response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout
                             || (int)response.StatusCode >= 500;
            throw new ClipTranscriptionException(
                $"Transcription returned {(int)response.StatusCode}: {ErrorMessage(body)}", retryable);
        }

        return Parse(body);
    }

    internal static JsonObject BuildRequest(byte[] audio, string model, TranscriptionPrompt prompt)
    {
        JsonObject request = new()
        {
            ["model"] = model,
            ["input_audio"] = new JsonObject
            {
                ["data"] = Convert.ToBase64String(audio),
                ["format"] = "m4a"
            }
        };

        JsonObject? providerOptions = ProviderOptions(model, prompt);
        if (providerOptions is not null)
        {
            request["provider"] = new JsonObject { ["options"] = providerOptions };
        }

        return request;
    }

    /// <summary>
    /// What each model family is sent beyond the audio. Gemini takes the vocabulary as <c>custom_vocabulary</c>:
    /// without it, <c>gemini-3.5-transcribe</c> returned nothing for some clips with speech. OpenAI models take the
    /// prompt and <c>chunking_strategy=auto</c>, and <c>gpt-transcribe</c> also takes <c>keywords</c>. Deepgram's
    /// integration only forwards formatting flags. <see cref="TranscriptionPrompt.For"/> leaves out whatever a model
    /// is not sent, so the stored run matches the request.
    /// </summary>
    internal static JsonObject? ProviderOptions(string model, TranscriptionPrompt prompt)
    {
        if (model.StartsWith("google/", StringComparison.OrdinalIgnoreCase))
        {
            return prompt.Keywords.Count == 0
                ? null
                : new JsonObject
                {
                    ["google-ai-studio"] = new JsonObject { ["custom_vocabulary"] = ToArray(prompt.Keywords) }
                };
        }

        if (model.StartsWith("openai/", StringComparison.OrdinalIgnoreCase))
        {
            JsonObject openai = new() { ["chunking_strategy"] = "auto" };
            if (prompt.Prompt is not null)
            {
                openai["prompt"] = prompt.Prompt;
            }

            if (prompt.Keywords.Count > 0)
            {
                openai["keywords"] = ToArray(prompt.Keywords);
            }

            return new JsonObject { ["openai"] = openai };
        }

        if (model.StartsWith("deepgram/", StringComparison.OrdinalIgnoreCase))
        {
            return new JsonObject { ["deepgram"] = new JsonObject { ["smart_format"] = true } };
        }

        return null;
    }

    /// <summary>Models whose provider takes a list of terms to spell as given.</summary>
    public static bool SupportsKeywords(string model)
    {
        return model.StartsWith("google/", StringComparison.OrdinalIgnoreCase)
               || model.StartsWith("openai/gpt-transcribe", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Models whose provider takes free-text context.</summary>
    public static bool SupportsPrompt(string model)
    {
        return model.StartsWith("openai/", StringComparison.OrdinalIgnoreCase);
    }

    internal static ClipTranscript Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        string text = root.TryGetProperty("text", out JsonElement textElement) ? textElement.GetString() ?? "" : "";
        List<string> languages = [];
        if (root.TryGetProperty("language", out JsonElement language) && language.ValueKind == JsonValueKind.String)
        {
            languages.Add(language.GetString()!);
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

    private static JsonArray ToArray(IEnumerable<string> values)
    {
        return new JsonArray(values.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
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
