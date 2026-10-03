namespace Reelshelf.ClipSummary;

/// <summary>
/// <c>ClipSummary</c> configuration. <see cref="Provider"/>, <see cref="Model"/> and <see cref="ReasoningEffort"/>
/// are the defaults for automatic runs; an admin can queue a run against any configured provider and model to
/// compare them.
/// </summary>
public sealed class ClipSummaryOptions
{
    public const string SectionName = "ClipSummary";

    /// <summary>Queue a run when a clip's automatic transcription succeeds.</summary>
    public bool AutoQueue { get; set; }

    public string Provider { get; set; } = ClipSummarizerFactory.OpenAI;
    public string Model { get; set; } = "";

    /// <summary>
    /// Reasoning effort for automatic runs, one of <c>LegendReasoningEffort.All</c>. Empty leaves it to the model's
    /// default.
    /// </summary>
    public string ReasoningEffort { get; set; } = "";

    public int MaxAttempts { get; set; } = 3;

    /// <summary>How many runs each API instance processes at once. Read when the API starts.</summary>
    public int Concurrency { get; set; } = 2;

    /// <summary>
    /// The OpenAI embedding model, asked for <see cref="ClipSummarizerFactory.EmbeddingDimensions"/> dimensions to
    /// fit the <c>vector</c> column. Embeddings always use the <c>openai</c> provider's key.
    /// </summary>
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";

    /// <summary>Credentials per provider, e.g. <c>ClipSummary:Providers:openai:ApiKey</c>.</summary>
    public Dictionary<string, ClipSummaryProviderOptions> Providers { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ClipSummaryProviderOptions
{
    public string ApiKey { get; set; } = "";
}
