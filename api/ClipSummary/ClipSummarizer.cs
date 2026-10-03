using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Responses;

namespace Reelshelf.ClipSummary;

/// <summary>
/// The prompt and response schema embedded from <c>Resources/</c>, loaded once. <see cref="PromptVersion"/> is a
/// hash of both and the summarizer's request layout, recorded on each run so results from different prompts are
/// never compared as the same.
/// </summary>
public sealed class ClipSummaryResources
{
    public string Prompt { get; }
    public JsonElement ResponseSchema { get; }
    public string PromptVersion { get; }

    public ClipSummaryResources()
    {
        byte[] prompt = Read("ClipSummary.prompt.md");
        byte[] schema = Read("ClipSummary.response-schema.json");

        Prompt = Encoding.UTF8.GetString(prompt).Trim();
        using (JsonDocument document = JsonDocument.Parse(schema))
        {
            ResponseSchema = document.RootElement.Clone();
        }

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(prompt);
        hash.AppendData(schema);
        hash.AppendData(Encoding.UTF8.GetBytes(ClipSummaryInput.RequestLayoutVersion));
        PromptVersion = Convert.ToHexStringLower(hash.GetHashAndReset())[..12];
    }

    private static byte[] Read(string name)
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
                              ?? throw new InvalidOperationException($"Embedded resource {name} is missing");
        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}

/// <summary>
/// The mood tags a summary may use, as listed in the prompt and the schema's enum. Keep the three in step; any
/// other tag the model returns is discarded.
/// </summary>
public static class ClipMoodTags
{
    public static readonly IReadOnlyList<string> All =
        ["funny", "clutch", "fail", "rage", "hype", "wholesome", "chaotic", "chill"];
}

/// <summary>What the summarizer is told about a clip besides the prompt.</summary>
public sealed record ClipSummaryInput(
    string? GameName,
    string? Title,
    DateTimeOffset ClipDate,
    string? PlayerLegend,
    bool? HasAudio,
    string Transcript)
{
    /// <summary>
    /// Bump whenever the message built here changes in a way the model sees, so runs are told apart by
    /// <see cref="ClipSummaryResources.PromptVersion"/> just as a changed prompt file would be.
    /// </summary>
    public const string RequestLayoutVersion = "1";

    /// <summary>A transcript with fewer words than this is called out as having little speech to go on.</summary>
    public const int NearEmptyWords = 5;

    public string ToMessage()
    {
        StringBuilder message = new();
        message.AppendLine($"Game: {(string.IsNullOrWhiteSpace(GameName) ? "unknown" : GameName.Trim())}");
        message.AppendLine($"Title: {(string.IsNullOrWhiteSpace(Title) ? "untitled" : Title.Trim())}");
        message.AppendLine($"Date: {ClipDate.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)}");
        if (!string.IsNullOrWhiteSpace(PlayerLegend))
        {
            message.AppendLine($"Legend the owner was playing (detected from the HUD): {PlayerLegend.Trim()}");
        }

        message.AppendLine();
        string transcript = Transcript.Trim();
        int words = transcript.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        if (HasAudio == false)
        {
            message.Append("Transcript: none. The clip has no audio track, so there is no speech or game sound to go on.");
        }
        else if (words == 0)
        {
            message.Append("Transcript: empty. No words were heard: nobody spoke, or only game sounds and music played.");
        }
        else
        {
            message.AppendLine(words < NearEmptyWords
                ? $"Transcript (only {words} {(words == 1 ? "word" : "words")}, so there is little speech to go on):"
                : "Transcript:");
            message.AppendLine("\"\"\"");
            message.AppendLine(transcript);
            message.Append("\"\"\"");
        }

        return message.ToString();
    }
}

/// <summary>
/// The model's answer, in the shape of <c>Resources/response-schema.json</c>, after
/// <see cref="Parse"/> has dropped anything that breaks the rules the schema can't enforce.
/// </summary>
public sealed record ClipSummaryResult(
    string Description,
    List<string> MoodTags,
    List<string> Quotes,
    List<string> People)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    /// <summary>
    /// Parses a response, keeping only mood tags from <see cref="ClipMoodTags.All"/> and quotes that appear in
    /// <paramref name="transcript"/> (ignoring case, punctuation and spacing). Throws <see cref="JsonException"/>
    /// when the response does not match the schema or has no description.
    /// </summary>
    public static ClipSummaryResult Parse(string json, string transcript)
    {
        Raw raw = JsonSerializer.Deserialize<Raw>(json, JsonOptions) ?? throw new JsonException("Response was null");
        string description = raw.Description?.Trim() ?? "";
        if (description.Length == 0)
        {
            throw new JsonException("Response has no description");
        }

        HashSet<string> tags = (raw.MoodTags ?? [])
            .Select(tag => tag.Trim().ToLowerInvariant())
            .ToHashSet();
        string searchableTranscript = Normalize(transcript);
        return new ClipSummaryResult(
            description,
            ClipMoodTags.All.Where(tags.Contains).ToList(),
            (raw.Quotes ?? [])
                .Select(quote => quote.Trim())
                .Where(quote => Normalize(quote) is { Length: > 0 } normalized && searchableTranscript.Contains(normalized))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            (raw.People ?? [])
                .Select(person => person.Trim())
                .Where(person => person.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList());
    }

    /// <summary>The text that is embedded: the description, then the mood tags.</summary>
    public string EmbeddingText()
    {
        return MoodTags.Count == 0 ? Description : $"{Description}\nMood: {string.Join(", ", MoodTags)}";
    }

    public string QuotesJson()
    {
        return JsonSerializer.Serialize(Quotes);
    }

    public static List<string> ParseQuotes(string json)
    {
        return JsonSerializer.Deserialize<List<string>>(json) ?? [];
    }

    /// <summary>Lower-case words separated by single spaces, with punctuation dropped.</summary>
    internal static string Normalize(string text)
    {
        StringBuilder normalized = new(text.Length);
        bool pendingSpace = false;
        foreach (char c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                if (pendingSpace && normalized.Length > 0)
                {
                    normalized.Append(' ');
                }

                normalized.Append(char.ToLowerInvariant(c));
                pendingSpace = false;
            }
            else if (char.IsWhiteSpace(c))
            {
                pendingSpace = true;
            }
        }

        return normalized.ToString();
    }

    private sealed record Raw(string? Description, List<string>? MoodTags, List<string>? Quotes, List<string>? People);
}

/// <remarks><see cref="CachedInputTokens"/> is the part of <see cref="InputTokens"/> served from the provider's cache.</remarks>
public sealed record ClipSummarization(
    ClipSummaryResult Result,
    string RawResponse,
    long? InputTokens,
    long? CachedInputTokens,
    long? OutputTokens);

public sealed record ClipEmbedding(ReadOnlyMemory<float> Vector, long? InputTokens);

/// <summary>Summarises a clip using one particular model.</summary>
public interface IClipSummarizer
{
    Task<ClipSummarization> SummarizeAsync(ClipSummaryInput input, CancellationToken cancellationToken);
}

/// <summary>Embeds a summary for semantic search.</summary>
public interface IClipEmbedder
{
    Task<ClipEmbedding> EmbedAsync(string text, CancellationToken cancellationToken);
}

/// <summary>Works with any provider that has a Microsoft.Extensions.AI <see cref="IChatClient"/>.</summary>
public sealed class ChatClientClipSummarizer(IChatClient chatClient, ClipSummaryResources resources) : IClipSummarizer
{
    public async Task<ClipSummarization> SummarizeAsync(ClipSummaryInput input, CancellationToken cancellationToken)
    {
        List<ChatMessage> messages =
        [
            new(ChatRole.System, resources.Prompt),
            new(ChatRole.User, input.ToMessage())
        ];
        ChatOptions options = new()
        {
            ResponseFormat = ChatResponseFormat.ForJsonSchema(resources.ResponseSchema, "clip_summary")
        };

        ChatResponse response = await chatClient.GetResponseAsync(messages, options, cancellationToken);
        string raw = response.Text;

        ClipSummaryResult result;
        try
        {
            result = ClipSummaryResult.Parse(raw, input.Transcript);
        }
        catch (JsonException ex)
        {
            // Kept on the failed run so a malformed answer can be inspected.
            ex.Data["RawResponse"] = raw;
            throw;
        }

        return new ClipSummarization(result, raw, response.Usage?.InputTokenCount,
            response.Usage?.CachedInputTokenCount, response.Usage?.OutputTokenCount);
    }
}

public sealed class ChatClientClipEmbedder(IEmbeddingGenerator<string, Embedding<float>> generator) : IClipEmbedder
{
    public async Task<ClipEmbedding> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        GeneratedEmbeddings<Embedding<float>> embeddings = await generator.GenerateAsync(
            [text],
            new EmbeddingGenerationOptions { Dimensions = ClipSummarizerFactory.EmbeddingDimensions },
            cancellationToken);
        Embedding<float> embedding = embeddings.Single();
        if (embedding.Vector.Length != ClipSummarizerFactory.EmbeddingDimensions)
        {
            throw new InvalidOperationException(
                $"The embedding has {embedding.Vector.Length} dimensions, not {ClipSummarizerFactory.EmbeddingDimensions}");
        }

        return new ClipEmbedding(embedding.Vector, embeddings.Usage?.InputTokenCount);
    }
}

/// <summary>
/// Builds summarizers for a provider and model, and the embedder. Adding a provider means adding a case to
/// <see cref="Create"/> and giving it an API key in configuration.
/// </summary>
public interface IClipSummarizerFactory
{
    bool IsConfigured(string provider);

    /// <summary>Whether the embedding provider has a key and an embedding model is set.</summary>
    bool IsEmbeddingConfigured { get; }

    /// <param name="reasoningEffort">One of <c>LegendReasoningEffort.All</c>, or null for the model's default.</param>
    IClipSummarizer Create(string provider, string model, string? reasoningEffort);

    IClipEmbedder CreateEmbedder(string model);
}

public sealed class ClipSummarizerFactory(
    IOptionsMonitor<ClipSummaryOptions> options,
    ClipSummaryResources resources) : IClipSummarizerFactory
{
    public const string OpenAI = "openai";

    /// <summary>The size of <c>clip_summary_run.embedding</c>.</summary>
    public const int EmbeddingDimensions = 1536;

    private static readonly string[] SupportedProviders = [OpenAI];

    private static readonly OpenAIClientOptions ClientOptions = new() { NetworkTimeout = TimeSpan.FromMinutes(2) };

    public bool IsConfigured(string provider)
    {
        return SupportedProviders.Contains(provider, StringComparer.OrdinalIgnoreCase)
               && !string.IsNullOrWhiteSpace(ApiKey(provider));
    }

    public bool IsEmbeddingConfigured =>
        IsConfigured(OpenAI) && !string.IsNullOrWhiteSpace(options.CurrentValue.EmbeddingModel);

    public IClipSummarizer Create(string provider, string model, string? reasoningEffort)
    {
        if (!IsConfigured(provider))
        {
            throw new InvalidOperationException($"Clip summary provider '{provider}' is not supported or has no API key");
        }

        IChatClient chatClient = provider.ToLowerInvariant() switch
        {
            OpenAI => CreateOpenAIChatClient(model, ApiKey(provider)!, reasoningEffort),
            _ => throw new InvalidOperationException($"Unknown clip summary provider '{provider}'")
        };
        return new ChatClientClipSummarizer(chatClient, resources);
    }

    public IClipEmbedder CreateEmbedder(string model)
    {
        if (!IsConfigured(OpenAI))
        {
            throw new InvalidOperationException("Clip summary embeddings need ClipSummary:Providers:openai:ApiKey");
        }

        IEmbeddingGenerator<string, Embedding<float>> generator =
            new OpenAIClient(new System.ClientModel.ApiKeyCredential(ApiKey(OpenAI)!), ClientOptions)
                .GetEmbeddingClient(model)
                .AsIEmbeddingGenerator();
        return new ChatClientClipEmbedder(generator);
    }

    /// <summary>
    /// Uses the Responses API, as legend detection does, asking OpenAI not to store the response. Every request
    /// starts with the same prompt and carries the same cache key, so the prompt is cached across clips.
    /// </summary>
    // OPENAI001: the SDK still marks its Responses API client as evaluation-only.
#pragma warning disable OPENAI001
    private static IChatClient CreateOpenAIChatClient(string model, string apiKey, string? reasoningEffort)
    {
        return new OpenAIClient(new System.ClientModel.ApiKeyCredential(apiKey), ClientOptions)
            .GetResponsesClient()
            .AsIChatClient(model)
            .AsBuilder()
            .ConfigureOptions(chatOptions => chatOptions.RawRepresentationFactory = _ =>
            {
                CreateResponseOptions request = new()
                {
                    PromptCacheKey = "reelshelf-clip-summary",
                    StoredOutputEnabled = false
                };
                if (reasoningEffort is not null)
                {
                    request.ReasoningOptions = new ResponseReasoningOptions
                    {
                        ReasoningEffortLevel = new ResponseReasoningEffortLevel(reasoningEffort)
                    };
                }

                return request;
            })
            .Build();
    }
#pragma warning restore OPENAI001

    private string? ApiKey(string provider)
    {
        return options.CurrentValue.Providers.TryGetValue(provider, out ClipSummaryProviderOptions? providerOptions)
            ? providerOptions.ApiKey
            : null;
    }
}
