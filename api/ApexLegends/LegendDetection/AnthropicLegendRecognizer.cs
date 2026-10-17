using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic;
using Anthropic.Models.Messages;

namespace Reelshelf.ApexLegends.LegendDetection;

/// <summary>
/// Identifies the clip owner's legend with a Claude model on Anthropic's Messages API, through the official SDK. It
/// sends the same prompt, schema and images as <see cref="ChatClientLegendRecognizer"/>, except that the reference
/// sheet goes in halves, since Claude would otherwise scale the 3,050-pixel sheet down until its labels blur. A cache
/// breakpoint after the sheet lets every clip reuse the prompt and sheet. Thinking is left to the model's default
/// (adaptive on current models) and the reasoning effort becomes <c>output_config.effort</c>. No refusal fallback is
/// asked for, so a run always answers with the model it names; a refusal fails the run.
/// </summary>
public sealed class AnthropicLegendRecognizer(
    AnthropicClient client,
    string model,
    string? reasoningEffort,
    LegendDetectionResources resources) : ILegendRecognizer
{
    /// <summary>
    /// Bump whenever the request built here changes in a way the model sees; it is part of
    /// <see cref="LegendDetectionResources.AnthropicPromptVersion"/>.
    /// </summary>
    public const string RequestLayoutVersion = "1";

    /// <summary>Room for adaptive thinking ahead of the short JSON answer.</summary>
    public const int MaxTokens = 16000;

    public string PromptVersion => resources.AnthropicPromptVersion;

    public async Task<LegendRecognition> RecognizeAsync(
        IReadOnlyList<LegendScreenshot> screenshots,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<LegendFrame> sheetHalves = await resources.ReferenceSheetHalvesAsync(cancellationToken);
        Message message = await client.Messages.Create(BuildRequest(sheetHalves, screenshots), cancellationToken);

        string raw = string.Concat(message.Content.Select(block => block.Value).OfType<TextBlock>().Select(text => text.Text));
        if (message.StopReason == StopReason.Refusal)
        {
            InvalidOperationException refusal = new($"{model} refused the request");
            refusal.Data["RawResponse"] = raw;
            throw refusal;
        }

        LegendDetectionResult result;
        try
        {
            result = LegendDetectionResult.Parse(raw);
        }
        catch (JsonException ex)
        {
            // Kept on the failed run so a malformed or cut-off answer can be inspected.
            ex.Data["RawResponse"] = raw;
            throw;
        }

        // Anthropic counts cache reads and writes apart from input_tokens; the usage table expects them included.
        Usage usage = message.Usage;
        long cacheRead = usage.CacheReadInputTokens ?? 0;
        long inputTokens = usage.InputTokens + cacheRead + (usage.CacheCreationInputTokens ?? 0);
        return new LegendRecognition(result, raw, inputTokens, cacheRead, usage.OutputTokens);
    }

    internal MessageCreateParams BuildRequest(
        IReadOnlyList<LegendFrame> sheetHalves,
        IReadOnlyList<LegendScreenshot> screenshots)
    {
        List<ContentBlockParam> content =
        [
            Text("Reference sheet, left half:"),
            Image(sheetHalves[0]),
            Text("Reference sheet, right half:"),
            Image(sheetHalves[1]),
            new TextBlockParam
            {
                Text = "Screenshots from the clip follow.",
                CacheControl = new CacheControlEphemeral()
            }
        ];
        for (int i = 0; i < screenshots.Count; i++)
        {
            LegendScreenshot screenshot = screenshots[i];
            content.Add(Text($"Screenshot {i + 1} of {screenshots.Count}, full screen:"));
            content.Add(Image(screenshot.Overview));
            content.Add(Text($"Screenshot {i + 1} of {screenshots.Count}, close-up of the bottom-left HUD panel:"));
            content.Add(Image(screenshot.Panel));
        }

        OutputConfig outputConfig = new() { Format = new JsonOutputFormat { Schema = Schema(resources.ResponseSchema) } };
        if (reasoningEffort is not null)
        {
            outputConfig = outputConfig with { Effort = Effort(reasoningEffort) };
        }

        return new MessageCreateParams
        {
            Model = model,
            MaxTokens = MaxTokens,
            System = resources.Prompt,
            Messages = [new MessageParam { Role = Role.User, Content = content }],
            OutputConfig = outputConfig
        };
    }

    /// <summary>
    /// The response schema without <c>minimum</c> and <c>maximum</c>, which structured outputs don't take. The
    /// confidence is still asked for as a number between 0 and 1 in the prompt.
    /// </summary>
    internal static Dictionary<string, JsonElement> Schema(JsonElement schema)
    {
        JsonNode node = JsonNode.Parse(schema.GetRawText())!;
        RemoveBounds(node);
        return node.AsObject().ToDictionary(
            property => property.Key,
            property => JsonSerializer.SerializeToElement(property.Value));
    }

    private static void RemoveBounds(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                obj.Remove("minimum");
                obj.Remove("maximum");
                foreach ((string _, JsonNode? child) in obj.ToList())
                {
                    RemoveBounds(child);
                }

                break;
            case JsonArray array:
                foreach (JsonNode? child in array)
                {
                    RemoveBounds(child);
                }

                break;
        }
    }

    /// <summary>
    /// Anthropic's effort levels are low to max; <c>none</c> and <c>minimal</c> have no equivalent and fail the run.
    /// </summary>
    private static Effort Effort(string effort)
    {
        return effort switch
        {
            "low" => Anthropic.Models.Messages.Effort.Low,
            "medium" => Anthropic.Models.Messages.Effort.Medium,
            "high" => Anthropic.Models.Messages.Effort.High,
            "xhigh" => Anthropic.Models.Messages.Effort.Xhigh,
            _ => throw new ArgumentException($"Anthropic models take no '{effort}' reasoning effort; use low to xhigh")
        };
    }

    private static TextBlockParam Text(string text)
    {
        return new TextBlockParam { Text = text };
    }

    private static ImageBlockParam Image(LegendFrame frame)
    {
        return new ImageBlockParam
        {
            Source = new Base64ImageSource
            {
                Data = Convert.ToBase64String(frame.Data),
                MediaType = frame.MediaType
            }
        };
    }
}
