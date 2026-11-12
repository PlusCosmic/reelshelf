namespace Reelshelf.ClipSearch;

/// <summary>
/// <c>ClipSearch</c> configuration: the chat model that reads a search into filters and a phrase, and how results are
/// cut off. The phrase is embedded with <c>ClipSummary:EmbeddingModel</c>, so it is compared with summaries embedded by
/// the same model. Search answers "not available" until <see cref="Model"/> is set and the provider has a key.
/// </summary>
public sealed class ClipSearchOptions
{
    public const string SectionName = "ClipSearch";

    public string Provider { get; set; } = ClipQueryParserFactory.OpenAI;
    public string Model { get; set; } = "";

    /// <summary>One of <c>LegendReasoningEffort.All</c>; empty leaves it to the model's default.</summary>
    public string ReasoningEffort { get; set; } = "";

    /// <summary>The most clips one search answers.</summary>
    public int MaxResults { get; set; } = 50;

    /// <summary>
    /// The largest cosine distance (0 the same, 2 opposite) between the phrase and a clip's summary for the clip to
    /// match, so a phrase never drags in clips that have nothing to do with it.
    /// </summary>
    public double MaxDistance { get; set; } = 0.8;

    /// <summary>Searches each account may run a minute; each one is a paid model call and an embedding.</summary>
    public int RequestsPerMinute { get; set; } = 20;

    /// <summary>
    /// Credentials per provider, e.g. <c>ClipSearch:Providers:openai:ApiKey</c>. A provider without one here uses
    /// <c>ClipSummary:Providers:&lt;provider&gt;:ApiKey</c>, so one key can serve both.
    /// </summary>
    public Dictionary<string, ClipSearchProviderOptions> Providers { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ClipSearchProviderOptions
{
    public string ApiKey { get; set; } = "";
}
