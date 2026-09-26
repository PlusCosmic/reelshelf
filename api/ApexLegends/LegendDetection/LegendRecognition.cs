using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;

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
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Credentials per provider, e.g. <c>LegendDetection:Providers:openai:ApiKey</c>.</summary>
    public Dictionary<string, LegendDetectionProviderOptions> Providers { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class LegendDetectionProviderOptions
{
    public string ApiKey { get; set; } = "";
}

/// <summary>One image sent to the model.</summary>
public sealed record LegendFrame(byte[] Data, string MediaType);

/// <summary>The model's answer, in the shape of <c>Resources/response-schema.json</c>.</summary>
public sealed record LegendDetectionResult(bool HudDetected, DetectedPlayer Player, List<DetectedTeammate> Teammates)
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
        if (result.Player is null || result.Teammates is null)
        {
            throw new JsonException("Response is missing player or teammates");
        }

        (string? playerLegend, double playerConfidence) = OnSheet(result.Player.Legend, result.Player.LegendConfidence);
        return result with
        {
            Player = result.Player with { Legend = playerLegend, LegendConfidence = playerConfidence },
            Teammates = result.Teammates
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

    public string TeammatesJson()
    {
        return JsonSerializer.Serialize(Teammates, JsonOptions);
    }

    public static List<DetectedTeammate> ParseTeammates(string json)
    {
        return JsonSerializer.Deserialize<List<DetectedTeammate>>(json, JsonOptions) ?? [];
    }
}

public sealed record DetectedPlayer(string? Name, string? Legend, double NameConfidence, double LegendConfidence);

public sealed record DetectedTeammate(int Slot, string? Name, string? Legend, double NameConfidence, double LegendConfidence);

public sealed record LegendRecognition(
    LegendDetectionResult Result,
    string RawResponse,
    long? InputTokens,
    long? OutputTokens);

/// <summary>Identifies legends and player names from a clip's frames using one particular model.</summary>
public interface ILegendRecognizer
{
    Task<LegendRecognition> RecognizeAsync(IReadOnlyList<LegendFrame> frames, CancellationToken cancellationToken);
}

/// <summary>
/// Works with any provider that has a Microsoft.Extensions.AI <see cref="IChatClient"/>. The prompt and reference
/// sheet come first and never change, so providers that cache prompt prefixes only bill the frames in full.
/// </summary>
public sealed class ChatClientLegendRecognizer(IChatClient chatClient, LegendDetectionResources resources)
    : ILegendRecognizer
{
    public async Task<LegendRecognition> RecognizeAsync(
        IReadOnlyList<LegendFrame> frames,
        CancellationToken cancellationToken)
    {
        List<AIContent> content =
        [
            new TextContent("Reference sheet:"),
            new DataContent(resources.ReferenceSheet, "image/png")
        ];
        for (int i = 0; i < frames.Count; i++)
        {
            content.Add(new TextContent($"Screenshot {i + 1} of {frames.Count}:"));
            content.Add(new DataContent(frames[i].Data, frames[i].MediaType));
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

        return new LegendRecognition(result, raw, response.Usage?.InputTokenCount, response.Usage?.OutputTokenCount);
    }
}

/// <summary>
/// Builds a recognizer for a provider and model. Adding a provider means adding a case to
/// <see cref="CreateChatClient"/> and giving it an API key in configuration.
/// </summary>
public interface ILegendRecognizerFactory
{
    bool IsConfigured(string provider);
    ILegendRecognizer Create(string provider, string model);
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

    public ILegendRecognizer Create(string provider, string model)
    {
        if (!IsConfigured(provider))
        {
            throw new InvalidOperationException($"Legend detection provider '{provider}' is not supported or has no API key");
        }

        return new ChatClientLegendRecognizer(CreateChatClient(provider, model, ApiKey(provider)!), resources);
    }

    private static IChatClient CreateChatClient(string provider, string model, string apiKey)
    {
        return provider.ToLowerInvariant() switch
        {
            OpenAI => new OpenAIClient(
                    new System.ClientModel.ApiKeyCredential(apiKey),
                    new OpenAIClientOptions { NetworkTimeout = TimeSpan.FromMinutes(2) })
                .GetChatClient(model)
                .AsIChatClient(),
            _ => throw new InvalidOperationException($"Unknown legend detection provider '{provider}'")
        };
    }

    private string? ApiKey(string provider)
    {
        return options.CurrentValue.Providers.TryGetValue(provider, out LegendDetectionProviderOptions? providerOptions)
            ? providerOptions.ApiKey
            : null;
    }
}
