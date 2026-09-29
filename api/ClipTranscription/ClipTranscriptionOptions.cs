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
    /// An OpenRouter model id. On a sample of clips with voices under game audio, <c>gemini-3.5-transcribe</c> (with
    /// the game's vocabulary) was the most accurate and left silent clips empty; <c>gpt-transcribe</c> often returned
    /// nothing, <c>gpt-4o-transcribe</c> invented foreign words, and <c>whisper-1</c> invents text for silence.
    /// </summary>
    public string Model { get; set; } = "google/gemini-3.5-transcribe";

    /// <summary>
    /// Tried when <see cref="Model"/> returns no words for a clip that has audio, before the clip is recorded as
    /// having no speech. <c>deepgram/nova-3</c> caught every clip with speech in the same sample. Empty turns it off.
    /// </summary>
    public string FallbackModel { get; set; } = "deepgram/nova-3";

    /// <summary>OpenRouter API key. Nothing is transcribed until it is set.</summary>
    public string OpenRouterApiKey { get; set; } = "";

    /// <summary>The API root, overridable for tests.</summary>
    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1/";

    public int MaxAttempts { get; set; } = 3;

    /// <summary>How many runs each API instance transcribes at once. Read when the API starts.</summary>
    public int Concurrency { get; set; } = 2;

    /// <summary>Price per audio minute by model, for the usage estimate. A model not listed shows no cost.</summary>
    public Dictionary<string, decimal> CostPerMinuteUsd { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["google/gemini-3.5-transcribe"] = 0.003m,
        ["deepgram/nova-3"] = 0.0043m,
        ["openai/gpt-4o-transcribe"] = 0.006m,
        ["openai/gpt-transcribe"] = 0.0045m,
        // Runs made before transcription moved to OpenRouter recorded bare OpenAI model ids.
        ["gpt-transcribe"] = 0.0045m
    };

    public bool IsConfigured => !string.IsNullOrWhiteSpace(OpenRouterApiKey) && !string.IsNullOrWhiteSpace(Model);

    /// <summary>The fallback model, or null when it is off or the same as <see cref="Model"/>.</summary>
    public string? EffectiveFallbackModel =>
        string.IsNullOrWhiteSpace(FallbackModel)
        || string.Equals(FallbackModel.Trim(), Model.Trim(), StringComparison.OrdinalIgnoreCase)
            ? null
            : FallbackModel.Trim();
}
