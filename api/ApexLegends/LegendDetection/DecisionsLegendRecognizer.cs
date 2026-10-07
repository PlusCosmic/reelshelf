using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Reelshelf.ApexLegends.LegendDetection;

/// <summary>
/// Identifies the clip owner's legend with OpenAI's Decisions API (<c>POST /v1/decisions</c>) instead of a chat
/// model writing JSON. The same images go in, with <c>Resources/decisions-prompt.md</c> as the leading text, and two
/// questions come back: a predicate for whether the owner's HUD is visible, and a choice over
/// <see cref="ApexLegendNames.All"/> plus <see cref="Unidentifiable"/>. The legend's confidence is the probability the
/// API gives the chosen option, rather than a number the model writes about itself. The Decisions API has no
/// reasoning effort and the .NET SDK does not cover it yet, so this calls it over plain HTTP.
/// </summary>
public sealed class DecisionsLegendRecognizer(
    HttpClient httpClient,
    string apiKey,
    string model,
    LegendDetectionResources resources) : ILegendRecognizer
{
    public const string HttpClientName = "LegendDetection.Decisions";
    public const string Endpoint = "https://api.openai.com/v1/decisions";
    public const string DefaultModel = "gpt-6-luna";

    /// <summary>The choice for a clip whose owner's legend can't be told from the screenshots.</summary>
    public const string Unidentifiable = "unidentifiable";

    public const string HudQuestion = "hud_detected";
    public const string LegendQuestion = "legend";

    /// <summary>
    /// Bump whenever the request built here changes in a way the model sees, including the questions' wording;
    /// it is part of <see cref="LegendDetectionResources.DecisionsPromptVersion"/>.
    /// </summary>
    public const string RequestLayoutVersion = "1";

    private const string HudInstructions =
        "Is the clip owner's own squad panel visible in the bottom-left of at least one screenshot while they are " +
        "playing, rather than spectating a teammate or behind a menu?";

    private const string LegendInstructions =
        "Which legend is the clip owner playing? Match their portrait in the usable close-ups against the reference " +
        "sheet. Choose unidentifiable when the clip owner's own panel is never visible while they are playing, or " +
        "the portrait cannot be matched reliably.";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public string PromptVersion => resources.DecisionsPromptVersion;

    public async Task<LegendRecognition> RecognizeAsync(
        IReadOnlyList<LegendScreenshot> screenshots,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(BuildRequest(screenshots).ToJsonString(), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        string raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Decisions API returned {(int)response.StatusCode}: {Truncate(raw, 500)}", null, response.StatusCode);
        }

        try
        {
            return Read(raw);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // Kept on the failed run so a refusal or malformed answer can be inspected.
            ex.Data["RawResponse"] = raw;
            throw;
        }
    }

    internal JsonObject BuildRequest(IReadOnlyList<LegendScreenshot> screenshots)
    {
        JsonArray content =
        [
            Text(resources.DecisionsPrompt),
            Text("Reference sheet:"),
            Image(resources.ReferenceSheet, "image/png"),
            Text("Screenshots from the clip follow.")
        ];
        for (int i = 0; i < screenshots.Count; i++)
        {
            LegendScreenshot screenshot = screenshots[i];
            content.Add(Text($"Screenshot {i + 1} of {screenshots.Count}, full screen:"));
            content.Add(Image(screenshot.Overview.Data, screenshot.Overview.MediaType));
            content.Add(Text($"Screenshot {i + 1} of {screenshots.Count}, close-up of the bottom-left HUD panel:"));
            content.Add(Image(screenshot.Panel.Data, screenshot.Panel.MediaType));
        }

        JsonArray choices = [];
        foreach (string legend in ApexLegendNames.All)
        {
            choices.Add(new JsonObject
            {
                ["value"] = legend,
                ["description"] = $"The portrait labelled {legend} on the reference sheet."
            });
        }

        choices.Add(new JsonObject
        {
            ["value"] = Unidentifiable,
            ["description"] = "The clip owner's own panel is not visible while they play, or their portrait matches no legend reliably."
        });

        return new JsonObject
        {
            ["model"] = model,
            ["input"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = content }),
            ["questions"] = new JsonArray(
                new JsonObject
                {
                    ["type"] = "predicate",
                    ["name"] = HudQuestion,
                    ["instructions"] = HudInstructions
                },
                new JsonObject
                {
                    ["type"] = "choice",
                    ["name"] = LegendQuestion,
                    ["instructions"] = LegendInstructions,
                    ["choices"] = choices
                })
        };
    }

    /// <summary>
    /// Turns a Decisions response into a result. Throws <see cref="InvalidOperationException"/> when a question was
    /// refused or is missing, and <see cref="JsonException"/> when the response can't be read.
    /// </summary>
    internal static LegendRecognition Read(string raw)
    {
        DecisionsResponse response = JsonSerializer.Deserialize<DecisionsResponse>(raw, ReadOptions)
                                     ?? throw new JsonException("Response was null");
        DecisionAnswer hud = Answer(response, HudQuestion, "predicate");
        DecisionAnswer legend = Answer(response, LegendQuestion, "choice");

        double hudProbability = hud.Probability ?? throw new JsonException($"{HudQuestion} has no probability");
        string choice = legend.Choice ?? throw new JsonException($"{LegendQuestion} has no choice");
        double? choiceProbability = legend.Probabilities?
            .FirstOrDefault(option => string.Equals(option.Value, choice, StringComparison.Ordinal))?.Probability;

        LegendDetectionResult result = LegendDetectionResult.Create(
            hudProbability >= 0.5,
            choice == Unidentifiable ? null : choice,
            choiceProbability ?? legend.Confidence ?? 0);
        return new LegendRecognition(result, raw, response.Usage?.InputTokens,
            response.Usage?.InputTokensDetails?.CachedTokens, response.Usage?.OutputTokens);
    }

    private static DecisionAnswer Answer(DecisionsResponse response, string name, string type)
    {
        DecisionAnswer answer = response.Answers?.FirstOrDefault(answer => answer.Name == name)
                                ?? throw new InvalidOperationException($"Decisions API returned no answer for {name}");
        if (answer.Type == "refusal")
        {
            throw new InvalidOperationException($"Decisions API refused {name}");
        }

        return answer.Type == type
            ? answer
            : throw new JsonException($"{name} came back as {answer.Type}, expected {type}");
    }

    private static JsonObject Text(string text)
    {
        return new JsonObject { ["type"] = "input_text", ["text"] = text };
    }

    private static JsonObject Image(byte[] data, string mediaType)
    {
        return new JsonObject
        {
            ["type"] = "input_image",
            ["image_url"] = $"data:{mediaType};base64,{Convert.ToBase64String(data)}"
        };
    }

    private static string Truncate(string text, int length)
    {
        return text.Length <= length ? text : text[..length] + "…";
    }

    private sealed record DecisionsResponse(List<DecisionAnswer>? Answers, DecisionsUsage? Usage);

    private sealed record DecisionAnswer(
        string? Type,
        string? Name,
        double? Probability,
        string? Choice,
        List<DecisionProbability>? Probabilities,
        double? Confidence);

    private sealed record DecisionProbability(string? Value, double Probability);

    // Not documented yet; read when present so the usage table can show it.
    private sealed record DecisionsUsage(long? InputTokens, long? OutputTokens, DecisionsInputTokensDetails? InputTokensDetails);

    private sealed record DecisionsInputTokensDetails(long? CachedTokens);
}
