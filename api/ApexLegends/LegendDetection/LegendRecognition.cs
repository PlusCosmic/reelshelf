using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Responses;

namespace Reelshelf.ApexLegends.LegendDetection;

/// <summary>
/// <c>LegendDetection</c> configuration. <see cref="Provider"/> and <see cref="Model"/> are the defaults for
/// automatic runs; an admin can queue a run against any configured provider and model to compare them.
/// </summary>
public sealed class LegendDetectionOptions
{
    public const string SectionName = "LegendDetection";

    /// <summary>Queue a run when an Apex Legends clip finishes encoding.</summary>
    public bool AutoDetect { get; set; }

    public string Provider { get; set; } = LegendRecognizerFactory.OpenAI;
    public string Model { get; set; } = "";

    /// <summary>
    /// Reasoning effort for automatic runs, one of <see cref="LegendReasoningEffort.All"/>. Empty leaves it to the
    /// model's default.
    /// </summary>
    public string ReasoningEffort { get; set; } = "";

    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// A stronger model to fall back on, on the same provider, when an automatic run sees the clip owner's panel
    /// but returns no legend or one below <see cref="EscalateBelow"/>. Empty turns the fallback off.
    /// </summary>
    public string EscalationModel { get; set; } = "";

    /// <summary>Reasoning effort for fallback runs, like <see cref="ReasoningEffort"/>.</summary>
    public string EscalationReasoningEffort { get; set; } = "";

    public double EscalateBelow { get; set; } = 0.9;

    /// <summary>How many runs each API instance sends to the model at once. Read when the API starts.</summary>
    public int Concurrency { get; set; } = 4;

    /// <summary>Credentials per provider, e.g. <c>LegendDetection:Providers:openai:ApiKey</c>.</summary>
    public Dictionary<string, LegendDetectionProviderOptions> Providers { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class LegendDetectionProviderOptions
{
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// OpenAI only: <c>prompt_cache_options.mode</c>, which GPT-5.6 and later need before they cache a prompt.
    /// <c>explicit</c> places a breakpoint after the reference sheet, so every clip reuses the prompt and sheet;
    /// <c>implicit</c> lets OpenAI place one at the end of the last user message, after the clip's own frames,
    /// where it is never reused. Empty sends no caching options.
    /// </summary>
    public string PromptCacheMode { get; set; } = "explicit";
}

/// <summary>
/// The reasoning efforts a run can ask for, as OpenAI names them. Not every model accepts every level; a model that
/// rejects one fails the run with the provider's error.
/// </summary>
public static class LegendReasoningEffort
{
    public static readonly IReadOnlyList<string> All = ["none", "minimal", "low", "medium", "high", "xhigh"];

    /// <summary>
    /// The effort in <see cref="All"/>'s spelling, or null for the model's default when <paramref name="effort"/>
    /// is empty. Throws <see cref="ArgumentException"/> for anything else.
    /// </summary>
    public static string? Normalize(string? effort)
    {
        if (string.IsNullOrWhiteSpace(effort))
        {
            return null;
        }

        string normalized = effort.Trim().ToLowerInvariant();
        return All.Contains(normalized)
            ? normalized
            : throw new ArgumentException($"Unknown reasoning effort '{effort}'; expected one of {string.Join(", ", All)}");
    }
}

/// <summary>One image sent to the model.</summary>
public sealed record LegendFrame(byte[] Data, string MediaType);

/// <summary>
/// What the model is shown of a clip: one full screenshot for context, then a close-up of the clip owner's HUD
/// panel from each screenshot (see <see cref="LegendHudCropper"/>).
/// </summary>
public sealed record LegendClipImages(LegendFrame Context, IReadOnlyList<LegendFrame> OwnerPanels);

/// <summary>
/// The model's answer, in the shape of <c>Resources/response-schema.json</c>. Earlier prompts also asked for names
/// and teammates; <see cref="Teammates"/> is null when the prompt did not, so old answers still parse.
/// </summary>
public sealed record LegendDetectionResult(
    bool HudDetected,
    DetectedPlayer Player,
    List<DetectedTeammate>? Teammates = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    /// <summary>
    /// Parses a response and drops any legend that is not on the reference sheet, since the schema cannot stop
    /// the model inventing one. Throws <see cref="JsonException"/> when the response does not match the schema.
    /// </summary>
    public static LegendDetectionResult Parse(string json)
    {
        LegendDetectionResult result = JsonSerializer.Deserialize<LegendDetectionResult>(json, JsonOptions)
                                       ?? throw new JsonException("Response was null");
        if (result.Player is null)
        {
            throw new JsonException("Response is missing the player");
        }

        (string? playerLegend, double playerConfidence) = OnSheet(result.Player.Legend, result.Player.LegendConfidence);
        return result with
        {
            Player = result.Player with { Legend = playerLegend, LegendConfidence = playerConfidence },
            Teammates = result.Teammates?
                .Select(teammate =>
                {
                    (string? legend, double confidence) = OnSheet(teammate.Legend, teammate.LegendConfidence);
                    return teammate with { Legend = legend, LegendConfidence = confidence };
                })
                .ToList()
        };
    }

    private static (string? Legend, double Confidence) OnSheet(string? legend, double confidence)
    {
        string? canonical = ApexLegendNames.Canonicalize(legend);
        return (canonical, canonical is null ? 0 : confidence);
    }

    public string? TeammatesJson()
    {
        return Teammates is null ? null : JsonSerializer.Serialize(Teammates, JsonOptions);
    }

    public static List<DetectedTeammate> ParseTeammates(string json)
    {
        return JsonSerializer.Deserialize<List<DetectedTeammate>>(json, JsonOptions) ?? [];
    }
}

public sealed record DetectedPlayer(
    string? Legend,
    double LegendConfidence,
    string? Name = null,
    double? NameConfidence = null);

public sealed record DetectedTeammate(int Slot, string? Name, string? Legend, double NameConfidence, double LegendConfidence);

/// <remarks><see cref="CachedInputTokens"/> is the part of <see cref="InputTokens"/> served from the provider's cache.</remarks>
public sealed record LegendRecognition(
    LegendDetectionResult Result,
    string RawResponse,
    long? InputTokens,
    long? CachedInputTokens,
    long? OutputTokens);

/// <summary>Identifies the clip owner's legend from a clip's frames using one particular model.</summary>
public interface ILegendRecognizer
{
    Task<LegendRecognition> RecognizeAsync(LegendClipImages images, CancellationToken cancellationToken);
}

/// <summary>
/// Works with any provider that has a Microsoft.Extensions.AI <see cref="IChatClient"/>. The prompt and reference
/// sheet come first and never change, then a text part flagged with <see cref="CacheBreakpointKey"/> marks the end
/// of that shared prefix for providers whose caching needs breakpoints; each provider's client translates it.
/// </summary>
public sealed class ChatClientLegendRecognizer(IChatClient chatClient, LegendDetectionResources resources)
    : ILegendRecognizer
{
    public const string CacheBreakpointKey = "reelshelf.cache_breakpoint";

    /// <summary>
    /// Bump whenever the request built here changes in a way the model sees, so runs are told apart by
    /// <see cref="LegendDetectionResources.PromptVersion"/> just as a changed prompt file would be.
    /// </summary>
    public const string RequestLayoutVersion = "3";

    public async Task<LegendRecognition> RecognizeAsync(
        LegendClipImages images,
        CancellationToken cancellationToken)
    {
        List<AIContent> content =
        [
            new TextContent("Reference sheet:"),
            new DataContent(resources.ReferenceSheet, "image/png"),
            new TextContent("Images from the clip follow.")
            {
                AdditionalProperties = new AdditionalPropertiesDictionary { [CacheBreakpointKey] = true }
            },
            new TextContent("Full screenshot, for context:"),
            new DataContent(images.Context.Data, images.Context.MediaType)
        ];
        int count = images.OwnerPanels.Count;
        for (int i = 0; i < count; i++)
        {
            content.Add(new TextContent($"Close-up of the clip owner's HUD panel, screenshot {i + 1} of {count}:"));
            content.Add(new DataContent(images.OwnerPanels[i].Data, images.OwnerPanels[i].MediaType));
        }

        List<ChatMessage> messages =
        [
            new(ChatRole.System, resources.Prompt),
            new(ChatRole.User, content)
        ];
        ChatOptions options = new()
        {
            ResponseFormat = ChatResponseFormat.ForJsonSchema(resources.ResponseSchema, "legend_detection")
        };

        ChatResponse response = await chatClient.GetResponseAsync(messages, options, cancellationToken);
        string raw = response.Text;

        LegendDetectionResult result;
        try
        {
            result = LegendDetectionResult.Parse(raw);
        }
        catch (JsonException ex)
        {
            // Kept on the failed run so a malformed answer can be inspected.
            ex.Data["RawResponse"] = raw;
            throw;
        }

        return new LegendRecognition(result, raw, response.Usage?.InputTokenCount,
            response.Usage?.CachedInputTokenCount, response.Usage?.OutputTokenCount);
    }
}

/// <summary>
/// Builds a recognizer for a provider and model. Adding a provider means adding a case to
/// <see cref="CreateChatClient"/> and giving it an API key in configuration.
/// </summary>
public interface ILegendRecognizerFactory
{
    bool IsConfigured(string provider);
    /// <param name="reasoningEffort">One of <see cref="LegendReasoningEffort.All"/>, or null for the model's default.</param>
    ILegendRecognizer Create(string provider, string model, string? reasoningEffort);
}

public sealed class LegendRecognizerFactory(
    IOptionsMonitor<LegendDetectionOptions> options,
    LegendDetectionResources resources) : ILegendRecognizerFactory
{
    public const string OpenAI = "openai";

    private static readonly string[] SupportedProviders = [OpenAI];

    public bool IsConfigured(string provider)
    {
        return SupportedProviders.Contains(provider, StringComparer.OrdinalIgnoreCase)
               && !string.IsNullOrWhiteSpace(ApiKey(provider));
    }

    public ILegendRecognizer Create(string provider, string model, string? reasoningEffort)
    {
        if (!IsConfigured(provider))
        {
            throw new InvalidOperationException($"Legend detection provider '{provider}' is not supported or has no API key");
        }

        LegendDetectionProviderOptions providerOptions = ProviderOptions(provider)!;
        IChatClient chatClient = provider.ToLowerInvariant() switch
        {
            OpenAI => CreateOpenAIChatClient(
                model,
                providerOptions,
                reasoningEffort,
                new OpenAIClientOptions { NetworkTimeout = TimeSpan.FromMinutes(2) }),
            _ => throw new InvalidOperationException($"Unknown legend detection provider '{provider}'")
        };
        return new ChatClientLegendRecognizer(chatClient, resources);
    }

    /// <summary>
    /// Uses the Responses API, where OpenAI documents prompt caching for current models. Every request carries
    /// the same cache key so they are routed to the same cache, and asks OpenAI not to store the response.
    /// Microsoft.Extensions.AI's own reasoning option has no <c>minimal</c>, so the effort is set on the request.
    /// </summary>
    // OPENAI001: the SDK still marks its Responses API client as evaluation-only.
#pragma warning disable OPENAI001
    internal static IChatClient CreateOpenAIChatClient(
        string model,
        LegendDetectionProviderOptions providerOptions,
        string? reasoningEffort,
        OpenAIClientOptions clientOptions)
    {
        string cacheMode = providerOptions.PromptCacheMode.Trim();
        ChatClientBuilder builder = new OpenAIClient(
                new System.ClientModel.ApiKeyCredential(providerOptions.ApiKey),
                clientOptions)
            .GetResponsesClient()
            .AsIChatClient(model)
            .AsBuilder();
        if (cacheMode == "explicit")
        {
            builder.Use(inner => new OpenAICacheBreakpointChatClient(inner));
        }

        return builder
            .ConfigureOptions(chatOptions => chatOptions.RawRepresentationFactory = _ =>
            {
#pragma warning disable SCME0001 // JsonPatch: the SDK has no property for prompt_cache_options yet.
                CreateResponseOptions request = new()
                {
                    PromptCacheKey = "reelshelf-legend-detection",
                    StoredOutputEnabled = false
                };
                if (reasoningEffort is not null)
                {
                    request.ReasoningOptions = new ResponseReasoningOptions
                    {
                        ReasoningEffortLevel = new ResponseReasoningEffortLevel(reasoningEffort)
                    };
                }

                if (cacheMode.Length > 0)
                {
                    request.Patch.Set("$.prompt_cache_options.mode"u8, cacheMode);
                }
#pragma warning restore SCME0001
                return request;
            })
            .Build();
    }

    /// <summary>
    /// Turns text flagged with <see cref="ChatClientLegendRecognizer.CacheBreakpointKey"/> into an input_text part
    /// carrying <c>prompt_cache_breakpoint: { "mode": "explicit" }</c>, which the SDK can't express yet.
    /// </summary>
    private sealed class OpenAICacheBreakpointChatClient(IChatClient inner) : DelegatingChatClient(inner)
    {
        public override Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return base.GetResponseAsync(messages.Select(WithBreakpoints), options, cancellationToken);
        }

        private static ChatMessage WithBreakpoints(ChatMessage message)
        {
            if (!message.Contents.Any(IsBreakpoint))
            {
                return message;
            }

            ChatMessage copy = message.Clone();
            copy.Contents = message.Contents
                .Select(content => IsBreakpoint(content) ? Breakpoint((TextContent)content) : content)
                .ToList();
            return copy;
        }

        private static bool IsBreakpoint(AIContent content)
        {
            return content is TextContent
                   && content.AdditionalProperties?.ContainsKey(ChatClientLegendRecognizer.CacheBreakpointKey) == true;
        }

        private static TextContent Breakpoint(TextContent content)
        {
#pragma warning disable SCME0001
            ResponseContentPart part = ResponseContentPart.CreateInputTextPart(content.Text);
            part.Patch.Set("$.prompt_cache_breakpoint.mode"u8, "explicit");
#pragma warning restore SCME0001
            return new TextContent(content.Text) { RawRepresentation = part };
        }
    }
#pragma warning restore OPENAI001

    private string? ApiKey(string provider)
    {
        return ProviderOptions(provider)?.ApiKey;
    }

    private LegendDetectionProviderOptions? ProviderOptions(string provider)
    {
        return options.CurrentValue.Providers.TryGetValue(provider, out LegendDetectionProviderOptions? providerOptions)
            ? providerOptions
            : null;
    }
}
