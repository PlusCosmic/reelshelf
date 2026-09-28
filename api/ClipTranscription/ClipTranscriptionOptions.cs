namespace Reelshelf.ClipTranscription;

public sealed class ClipTranscriptionOptions
{
    public const string SectionName = "ClipTranscription";

    /// <summary>
    /// Queue a run when a clip owned by a whitelisted account finishes encoding. Dev and prod share the queue, so
    /// set it on the instances that should add work, not all of them.
    /// </summary>
    public bool AutoQueue { get; set; }

    public string Model { get; set; } = "gpt-transcribe";

    /// <summary>OpenAI API key. Nothing is transcribed until it is set.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>The API root, overridable for tests or a compatible proxy.</summary>
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";

    public int MaxAttempts { get; set; } = 3;

    /// <summary>How many runs each API instance transcribes at once. Read when the API starts.</summary>
    public int Concurrency { get; set; } = 2;

    /// <summary>Price per audio minute for the usage estimate; <c>gpt-transcribe</c> is $0.0045.</summary>
    public decimal CostPerMinuteUsd { get; set; } = 0.0045m;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(Model);
}
