using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using Reelshelf.ClipSummary;

namespace Reelshelf.ClipSearch;

/// <summary>The prompt and response schema embedded from <c>Resources/</c>, loaded once.</summary>
public sealed class ClipQueryParserResources
{
    public string Prompt { get; }
    public JsonElement ResponseSchema { get; }

    /// <summary>A hash of the prompt and schema, logged with each parse so answers from different prompts can be told apart.</summary>
    public string PromptVersion { get; }

    public ClipQueryParserResources()
    {
        byte[] prompt = Read("ClipSearch.prompt.md");
        byte[] schema = Read("ClipSearch.response-schema.json");

        Prompt = Encoding.UTF8.GetString(prompt).Trim();
        using (JsonDocument document = JsonDocument.Parse(schema))
        {
            ResponseSchema = document.RootElement.Clone();
        }

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(prompt);
        hash.AppendData(schema);
        hash.AppendData(Encoding.UTF8.GetBytes(ClipQueryInput.RequestLayoutVersion));
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

/// <summary>A game in the searcher's library, by the id the model answers with.</summary>
public sealed record ClipQueryGame(Guid Id, string Name);

/// <summary>What the model is told besides the prompt: today's date where the searcher is, their games and the search.</summary>
public sealed record ClipQueryInput(string Query, DateOnly Today, IReadOnlyList<ClipQueryGame> Games)
{
    /// <summary>Bump whenever the message built here changes in a way the model sees.</summary>
    public const string RequestLayoutVersion = "1";

    public string ToMessage()
    {
        StringBuilder message = new();
        message.AppendLine(
            $"Today: {Today.ToString("dddd d MMMM yyyy", CultureInfo.InvariantCulture)} ({Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)})");
        message.AppendLine();
        if (Games.Count == 0)
        {
            message.AppendLine("Games in the library: none yet.");
        }
        else
        {
            message.AppendLine("Games in the library (id: name):");
            foreach (ClipQueryGame game in Games)
            {
                message.AppendLine($"- {game.Id}: {game.Name}");
            }
        }

        message.AppendLine();
        message.AppendLine("Search:");
        message.AppendLine("\"\"\"");
        message.AppendLine(Query.Trim());
        message.Append("\"\"\"");
        return message.ToString();
    }
}

/// <summary>
/// A search read into filters and a phrase. Dates are whole days where the searcher is, both inclusive. Every list is
/// empty, never null, when the search doesn't use it.
/// </summary>
public sealed record ClipQueryFilters(
    DateOnly? StartDate,
    DateOnly? EndDate,
    IReadOnlyList<Guid> GameCategoryIds,
    IReadOnlyList<string> MoodTags,
    IReadOnlyList<string> People,
    string? Phrase)
{
    public const int MaxPeople = 5;
    public const int MaxPhraseLength = 200;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    /// <summary>
    /// Parses the model's answer, keeping only what the search can use: dates that are real days (swapped if they come
    /// backwards), ids of the searcher's own <paramref name="games"/>, tags from <see cref="ClipMoodTags.All"/> and
    /// non-empty names and phrase. Throws <see cref="JsonException"/> when the answer doesn't match the schema.
    /// </summary>
    public static ClipQueryFilters Parse(string json, IReadOnlyList<ClipQueryGame> games)
    {
        Raw raw = JsonSerializer.Deserialize<Raw>(json, JsonOptions) ?? throw new JsonException("Response was null");
        HashSet<Guid> ownGames = games.Select(game => game.Id).ToHashSet();
        List<Guid> gameIds = (raw.GameIds ?? [])
            .Select(id => Guid.TryParse(id, out Guid parsed) ? parsed : Guid.Empty)
            .Where(ownGames.Contains)
            .ToList();
        return Clean(ParseDate(raw.StartDate), ParseDate(raw.EndDate), gameIds, raw.MoodTags, raw.People, raw.Phrase);
    }

    /// <summary>
    /// Tidies filters the client sent back (a search with some of its filters taken away) the same way as a model's
    /// answer. Game ids are checked against the searcher's library in SQL, which only ever reads their own clips.
    /// </summary>
    public static ClipQueryFilters Clean(
        DateOnly? startDate,
        DateOnly? endDate,
        IEnumerable<Guid>? gameCategoryIds,
        IEnumerable<string>? moodTags,
        IEnumerable<string>? people,
        string? phrase)
    {
        if (startDate is { } start && endDate is { } end && start > end)
        {
            (startDate, endDate) = (end, start);
        }

        HashSet<string> tags = (moodTags ?? [])
            .Select(tag => tag.Trim().ToLowerInvariant())
            .ToHashSet();
        string? trimmedPhrase = string.IsNullOrWhiteSpace(phrase) ? null : phrase.Trim();
        if (trimmedPhrase is { Length: > MaxPhraseLength })
        {
            trimmedPhrase = trimmedPhrase[..MaxPhraseLength];
        }

        return new ClipQueryFilters(
            startDate,
            endDate,
            (gameCategoryIds ?? []).Where(id => id != Guid.Empty).Distinct().ToList(),
            ClipMoodTags.All.Where(tags.Contains).ToList(),
            (people ?? [])
                .Select(person => person.Trim())
                .Where(person => person.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxPeople)
                .ToList(),
            trimmedPhrase);
    }

    private static DateOnly? ParseDate(string? value)
    {
        return DateOnly.TryParseExact(value?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out DateOnly date)
            ? date
            : null;
    }

    private sealed record Raw(
        string? StartDate,
        string? EndDate,
        List<string>? GameIds,
        List<string>? MoodTags,
        List<string>? People,
        string? Phrase);
}

/// <summary>Reads a search into filters and a phrase.</summary>
public interface IClipQueryParser
{
    Task<ClipQueryFilters> ParseAsync(ClipQueryInput input, CancellationToken cancellationToken);
}

/// <summary>Works with any provider that has a Microsoft.Extensions.AI <see cref="IChatClient"/>.</summary>
public sealed class ChatClientClipQueryParser(IChatClient chatClient, ClipQueryParserResources resources)
    : IClipQueryParser
{
    public async Task<ClipQueryFilters> ParseAsync(ClipQueryInput input, CancellationToken cancellationToken)
    {
        List<ChatMessage> messages =
        [
            new(ChatRole.System, resources.Prompt),
            new(ChatRole.User, input.ToMessage())
        ];
        ChatOptions options = new()
        {
            ResponseFormat = ChatResponseFormat.ForJsonSchema(resources.ResponseSchema, "clip_search")
        };

        ChatResponse response = await chatClient.GetResponseAsync(messages, options, cancellationToken);
        return ClipQueryFilters.Parse(response.Text, input.Games);
    }
}

/// <summary>
/// Builds the configured query parser. Adding a provider means adding a case to <see cref="Create"/> and giving it an
/// API key in configuration.
/// </summary>
public interface IClipQueryParserFactory
{
    /// <summary>Whether a model is set and its provider is supported and has a key.</summary>
    bool IsConfigured { get; }

    IClipQueryParser Create();
}

public sealed class ClipQueryParserFactory(
    IOptionsMonitor<ClipSearchOptions> options,
    IOptionsMonitor<ClipSummaryOptions> summaryOptions,
    ClipQueryParserResources resources) : IClipQueryParserFactory
{
    public const string OpenAI = "openai";

    private static readonly string[] SupportedProviders = [OpenAI];

    // A search waits on this call, so it is cut short well before a person gives up.
    private static readonly OpenAIClientOptions ClientOptions = new() { NetworkTimeout = TimeSpan.FromSeconds(30) };

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(options.CurrentValue.Model)
        && SupportedProviders.Contains(options.CurrentValue.Provider, StringComparer.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(ApiKey(options.CurrentValue.Provider));

    public IClipQueryParser Create()
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Clip search needs ClipSearch:Model and a key for ClipSearch:Provider");
        }

        ClipSearchOptions current = options.CurrentValue;
        string? effort = string.IsNullOrWhiteSpace(current.ReasoningEffort) ? null : current.ReasoningEffort.Trim();
        IChatClient chatClient = current.Provider.ToLowerInvariant() switch
        {
            OpenAI => CreateOpenAIChatClient(current.Model, ApiKey(current.Provider)!, effort),
            _ => throw new InvalidOperationException($"Unknown clip search provider '{current.Provider}'")
        };
        return new ChatClientClipQueryParser(chatClient, resources);
    }

    /// <summary>
    /// Chat Completions, asking OpenAI not to store the response. A search is one short exchange, so the Responses API
    /// that summaries use buys nothing here. The effort goes through Microsoft.Extensions.AI's own reasoning option.
    /// </summary>
    private static IChatClient CreateOpenAIChatClient(string model, string apiKey, string? reasoningEffort)
    {
        ReasoningEffort? effort = ToReasoningEffort(reasoningEffort);
        return new OpenAIClient(new System.ClientModel.ApiKeyCredential(apiKey), ClientOptions)
            .GetChatClient(model)
            .AsIChatClient()
            .AsBuilder()
            .ConfigureOptions(chatOptions =>
            {
                chatOptions.RawRepresentationFactory = _ =>
                    new OpenAI.Chat.ChatCompletionOptions { StoredOutputEnabled = false };
                if (effort is { } level)
                {
                    chatOptions.Reasoning = new ReasoningOptions { Effort = level };
                }
            })
            .Build();
    }

    /// <summary>An effort in <c>LegendReasoningEffort.All</c>'s spelling; null (or one it doesn't know) leaves the model's default.</summary>
    internal static ReasoningEffort? ToReasoningEffort(string? effort)
    {
        return effort?.Trim().ToLowerInvariant() switch
        {
            "none" or "minimal" => ReasoningEffort.None,
            "low" => ReasoningEffort.Low,
            "medium" => ReasoningEffort.Medium,
            "high" => ReasoningEffort.High,
            "xhigh" => ReasoningEffort.ExtraHigh,
            _ => null
        };
    }

    private string? ApiKey(string provider)
    {
        if (options.CurrentValue.Providers.TryGetValue(provider, out ClipSearchProviderOptions? own)
            && !string.IsNullOrWhiteSpace(own.ApiKey))
        {
            return own.ApiKey;
        }

        return summaryOptions.CurrentValue.Providers.TryGetValue(provider, out ClipSummaryProviderOptions? shared)
            ? shared.ApiKey
            : null;
    }
}
