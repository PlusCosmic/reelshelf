namespace Reelshelf.ClipTranscription;

public sealed class ClipTranscriptionOptions
{
    public const string SectionName = "ClipTranscription";

    /// <summary>
    /// Queue a run when a clip owned by a whitelisted account finishes encoding. Dev and prod share the queue, so
    /// set it on the instances that should add work, not all of them.
    /// </summary>
    public bool AutoQueue { get; set; }

    /// <summary>
    /// <c>gpt-4o-transcribe</c> by default: on a sample of clips <c>gpt-transcribe</c> returned no text for about half
    /// of those with speech, even with chunking, while <c>gpt-4o-transcribe</c> transcribed nearly all of them and
    /// still returned nothing for clips without speech. <c>whisper-1</c> invents text for silent clips.
    /// </summary>
    public string Model { get; set; } = "gpt-4o-transcribe";

    /// <summary>OpenAI API key. Nothing is transcribed until it is set.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>The API root, overridable for tests or a compatible proxy.</summary>
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";

    public int MaxAttempts { get; set; } = 3;

    /// <summary>How many runs each API instance transcribes at once. Read when the API starts.</summary>
    public int Concurrency { get; set; } = 2;

    /// <summary>Price per audio minute by model, for the usage estimate. A model not listed shows no cost.</summary>
    public Dictionary<string, decimal> CostPerMinuteUsd { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gpt-4o-transcribe"] = 0.006m,
        ["gpt-4o-mini-transcribe"] = 0.003m,
        ["gpt-transcribe"] = 0.0045m,
        ["whisper-1"] = 0.006m
    };

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(Model);
}
