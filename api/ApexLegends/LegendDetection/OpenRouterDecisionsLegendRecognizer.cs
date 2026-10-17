using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Reelshelf.ApexLegends.LegendDetection;

/// <summary>
/// Identifies the clip owner's legend with a decisions model on OpenRouter's Decisions API
/// (<c>POST /api/alpha/decisions</c>), such as TypeSafe's Jev. It asks the same two questions as
/// <see cref="DecisionsLegendRecognizer"/>, in OpenRouter's shape: the prompt, sheet and screenshots go in
/// <c>state</c>, and the HUD question is a <c>noul</c> whose answer is the probability it is true. Jev takes images
/// of at most 1,568 pixels a side, so the reference sheet goes in halves. There is no reasoning effort and no prompt
/// caching to ask for.
/// </summary>
public sealed class OpenRouterDecisionsLegendRecognizer(
    HttpClient httpClient,
    string apiKey,
    string model,
    LegendDetectionResources resources) : ILegendRecognizer
{
    public const string HttpClientName = "LegendDetection.OpenRouterDecisions";
    public const string Endpoint = "https://openrouter.ai/api/alpha/decisions";
    public const string DefaultModel = "typesafe/jev-1.14";

    /// <summary>
    /// Bump whenever the request built here changes in a way the model sees; it is part of
    /// <see cref="LegendDetectionResources.OpenRouterDecisionsPromptVersion"/>.
    /// </summary>
    public const string RequestLayoutVersion = "1";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public string PromptVersion => resources.OpenRouterDecisionsPromptVersion;

    public async Task<LegendRecognition> RecognizeAsync(
        IReadOnlyList<LegendScreenshot> screenshots,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<LegendFrame> sheetHalves = await resources.ReferenceSheetHalvesAsync(cancellationToken);
        using HttpRequestMessage request = new(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(
                BuildRequest(sheetHalves, screenshots).ToJsonString(), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        string raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"OpenRouter Decisions API returned {(int)response.StatusCode}: {Truncate(raw, 500)}",
                null,
                response.StatusCode);
        }

        try
        {
            return Read(raw);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // Kept on the failed run so a malformed answer can be inspected.
            ex.Data["RawResponse"] = raw;
            throw;
        }
    }

    internal JsonObject BuildRequest(IReadOnlyList<LegendFrame> sheetHalves, IReadOnlyList<LegendScreenshot> screenshots)
    {
        // Text items in state are plain strings, not text parts.
        JsonArray state =
        [
            resources.DecisionsPrompt,
            "Reference sheet, left half:",
            Image(sheetHalves[0]),
            "Reference sheet, right half:",
            Image(sheetHalves[1]),
            "Screenshots from the clip follow."
        ];
        for (int i = 0; i < screenshots.Count; i++)
        {
            LegendScreenshot screenshot = screenshots[i];
            state.Add($"Screenshot {i + 1} of {screenshots.Count}, full screen:");
            state.Add(Image(screenshot.Overview));
            state.Add($"Screenshot {i + 1} of {screenshots.Count}, close-up of the bottom-left HUD panel:");
            state.Add(Image(screenshot.Panel));
        }

        JsonObject criteria = [];
        foreach (string legend in ApexLegendNames.All)
        {
            criteria[legend] = $"The portrait labelled {legend} on the reference sheet.";
        }

        criteria[DecisionsLegendRecognizer.Unidentifiable] =
            "The clip owner's own panel is not visible while they play, or their portrait matches no legend reliably.";

        return new JsonObject
        {
            ["model"] = model,
            ["state"] = state,
            ["questions"] = new JsonObject
            {
                [DecisionsLegendRecognizer.HudQuestion] = new JsonObject
                {
                    ["type"] = "noul",
                    ["instructions"] = DecisionsLegendRecognizer.HudInstructions
                },
                [DecisionsLegendRecognizer.LegendQuestion] = new JsonObject
                {
                    ["type"] = "choice",
                    ["instructions"] = DecisionsLegendRecognizer.LegendInstructions,
                    ["criteria"] = criteria
                }
            }
        };
    }

    /// <summary>
    /// Turns a response into a result. Throws <see cref="InvalidOperationException"/> when a question has no answer,
    /// and <see cref="JsonException"/> when the response can't be read.
    /// </summary>
    internal static LegendRecognition Read(string raw)
    {
        DecisionsResponse response = JsonSerializer.Deserialize<DecisionsResponse>(raw, ReadOptions)
                                     ?? throw new JsonException("Response was null");
        DecisionAnswer hud = Answer(response, DecisionsLegendRecognizer.HudQuestion, "noul");
        DecisionAnswer legend = Answer(response, DecisionsLegendRecognizer.LegendQuestion, "choice");

        double hudProbability = hud.Noul
                                ?? throw new JsonException($"{DecisionsLegendRecognizer.HudQuestion} has no probability");
        string choice = legend.Choice
                        ?? throw new JsonException($"{DecisionsLegendRecognizer.LegendQuestion} has no choice");
        double? choiceProbability = legend.Probabilities?.TryGetValue(choice, out double probability) == true
            ? probability
            : null;

        LegendDetectionResult result = LegendDetectionResult.Create(
            hudProbability >= 0.5,
            choice == DecisionsLegendRecognizer.Unidentifiable ? null : choice,
            choiceProbability ?? legend.Confidence ?? 0);
        return new LegendRecognition(result, raw, response.Usage?.InputTokens, null, response.Usage?.OutputTokens);
    }

    private static DecisionAnswer Answer(DecisionsResponse response, string name, string type)
    {
        if (response.Answers is null || !response.Answers.TryGetValue(name, out DecisionAnswer? answer))
        {
            throw new InvalidOperationException($"OpenRouter Decisions API returned no answer for {name}");
        }

        return answer.Type == type
            ? answer
            : throw new JsonException($"{name} came back as {answer.Type}, expected {type}");
    }

    private static JsonObject Image(LegendFrame frame)
    {
        return new JsonObject
        {
            ["type"] = "image_url",
            ["image_url"] = new JsonObject
            {
                ["url"] = $"data:{frame.MediaType};base64,{Convert.ToBase64String(frame.Data)}"
            }
        };
    }

    private static string Truncate(string text, int length)
    {
        return text.Length <= length ? text : text[..length] + "…";
    }

    private sealed record DecisionsResponse(Dictionary<string, DecisionAnswer>? Answers, DecisionsUsage? Usage);

    private sealed record DecisionAnswer(
        string? Type,
        double? Noul,
        string? Choice,
        Dictionary<string, double>? Probabilities,
        double? Confidence);

    private sealed record DecisionsUsage(long? InputTokens, long? OutputTokens);
}
