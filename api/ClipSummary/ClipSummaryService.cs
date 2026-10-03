using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Reelshelf.ApexLegends.LegendDetection;
using Reelshelf.ClipTranscription;
using Reelshelf.Core;
using Reelshelf.Exceptions;

namespace Reelshelf.ClipSummary;

public class ClipSummaryService(
    ClipSummaryStatements statements,
    ClipsStatements clipsStatements,
    IClipSummarizerFactory summarizerFactory,
    ClipSummaryResources resources,
    IOptionsMonitor<ClipSummaryOptions> options,
    ILogger<ClipSummaryService> logger)
{
    /// <summary>Retry a failed attempt after this long, so a brief provider outage is not retried at once.</summary>
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);

    /// <summary>A run still marked running after this long belonged to a worker that stopped; claim it again.</summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The summary trigger for a succeeded transcription, or null when it queues none. An automatic
    /// transcription queues the clip's automatic summary, and a retry that heard speech queues a <c>retry</c>
    /// summary, since the automatic one was made from the empty transcript it replaced. Manual transcriptions are
    /// for comparing models and queue nothing.
    /// </summary>
    public static string? TriggerForTranscription(string transcriptionTrigger, bool heardSpeech)
    {
        return transcriptionTrigger switch
        {
            ClipTranscriptionStatements.AutoTrigger => ClipSummaryStatements.AutoTrigger,
            ClipTranscriptionStatements.RetryTrigger when heardSpeech => ClipSummaryStatements.RetryTrigger,
            _ => null
        };
    }

    /// <summary>
    /// Queues a summary of a transcription run that just succeeded, when automatic queueing is on and summaries
    /// are configured. Safe to call more than once: a clip only ever gets one automatic run.
    /// </summary>
    public async Task QueueForTranscriptionAsync(
        Guid transcriptionRunId,
        Guid clipId,
        string transcriptionTrigger,
        bool heardSpeech)
    {
        ClipSummaryOptions current = options.CurrentValue;
        string? trigger = TriggerForTranscription(transcriptionTrigger, heardSpeech);
        if (trigger is null || !current.AutoQueue)
        {
            return;
        }

        (string provider, string model, string? effort, string embeddingModel) settings;
        try
        {
            settings = Resolve(null, null, null);
        }
        catch (BadRequestException ex)
        {
            logger.LogWarning("Not queueing a summary for clip {ClipId}: {Error}", clipId, ex.Message);
            return;
        }

        if (await statements.QueueRunForTranscriptionAsync(transcriptionRunId, trigger, settings.provider,
                settings.model, settings.effort, settings.embeddingModel))
        {
            logger.LogInformation("Queued {Trigger} summary for clip {ClipId}", trigger, clipId);
        }
    }

    /// <summary>Queues the automatic run for every clip with a succeeded transcript that has never had one.</summary>
    public async Task<int> BackfillAsync(string? reasoningEffort)
    {
        (string provider, string model, string? effort, string embeddingModel) = Resolve(null, null, reasoningEffort);
        int queued = await statements.QueueAutoRunsForTranscribedClipsAsync(provider, model, effort, embeddingModel);
        logger.LogInformation("Backfill queued summaries for {Count} clips", queued);
        return queued;
    }

    /// <summary>
    /// Queues an extra run on the clip's latest transcript, optionally with a different provider, model or
    /// reasoning effort to compare against.
    /// </summary>
    public async Task<ClipSummaryStatements.ClipSummaryRunRow> QueueManualRunAsync(
        Guid clipId,
        string? provider,
        string? model,
        string? reasoningEffort)
    {
        (string resolvedProvider, string resolvedModel, string? effort, string embeddingModel) =
            Resolve(provider, model, reasoningEffort);
        _ = await clipsStatements.GetClipById(clipId) ?? throw new NotFoundException("Clip", clipId);
        return await statements.QueueManualRunAsync(clipId, resolvedProvider, resolvedModel, effort, embeddingModel)
               ?? throw new BadRequestException("The clip has no transcript to summarise yet");
    }

    public async Task<List<ClipSummaryStatements.ClipSummaryRunRow>> GetRunsAsync(Guid clipId)
    {
        return await statements.GetRunsForClipAsync(clipId);
    }

    public async Task<List<ClipSummaryUsage>> GetUsageAsync()
    {
        return (await statements.GetUsageAsync())
            .Select(row => new ClipSummaryUsage(
                row.Provider,
                row.Model,
                row.ReasoningEffort,
                row.PromptVersion,
                (int)row.Succeeded,
                (int)row.Failed,
                (int)row.Queued,
                row.InputTokens,
                row.CachedInputTokens,
                row.OutputTokens,
                row.EmbeddingTokens,
                row.AverageDurationMs))
            .ToList();
    }

    /// <summary>Processes the next queued run. Returns false when there was nothing to do.</summary>
    /// <remarks>
    /// An instance that can't embed never claims a run, so its runs stay queued for one that can, as with
    /// transcription. A run is only marked succeeded once both the summary and its embedding are stored.
    /// </remarks>
    public async Task<bool> ProcessNextRunAsync(CancellationToken cancellationToken)
    {
        if (!summarizerFactory.IsEmbeddingConfigured)
        {
            return false;
        }

        ClipSummaryStatements.ClaimedRunRow? run = await statements.ClaimNextRunAsync(RetryAfter, StaleAfter);
        if (run is null)
        {
            return false;
        }

        int maxAttempts = options.CurrentValue.MaxAttempts;
        if (run.Attempts > maxAttempts)
        {
            await statements.FailRunAsync(run.Id, $"Gave up after {maxAttempts} attempts", null, null, retry: false);
            return true;
        }

        bool canRetry = run.Attempts < maxAttempts;
        if (!summarizerFactory.IsConfigured(run.Provider))
        {
            await statements.FailRunAsync(run.Id, $"Provider '{run.Provider}' is not configured", null, null,
                retry: false);
            return true;
        }

        ClipSummaryInput input = new(run.GameName, run.Title, run.ClipCreatedAt, run.PlayerLegend, run.HasAudio,
            run.Transcript ?? "");
        try
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            IClipSummarizer summarizer = summarizerFactory.Create(run.Provider, run.Model, run.ReasoningEffort);
            ClipSummarization summarization = await summarizer.SummarizeAsync(input, cancellationToken);
            ClipEmbedding embedding = await summarizerFactory.CreateEmbedder(run.EmbeddingModel)
                .EmbedAsync(summarization.Result.EmbeddingText(), cancellationToken);
            await statements.CompleteRunAsync(run.Id, resources.PromptVersion, summarization, embedding,
                (int)stopwatch.ElapsedMilliseconds);

            logger.LogInformation(
                "Summarised clip {ClipId} with {Provider}/{Model} ({ReasoningEffort}): {MoodTags}",
                run.ClipId, run.Provider, run.Model, run.ReasoningEffort ?? "default effort",
                string.Join(", ", summarization.Result.MoodTags));
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Summary attempt {Attempt} failed for clip {ClipId} with {Provider}/{Model}",
                run.Attempts, run.ClipId, run.Provider, run.Model);
            string? rawResponse = ex is JsonException ? ex.Data["RawResponse"] as string : null;
            await statements.FailRunAsync(run.Id, ex.Message, resources.PromptVersion, rawResponse, canRetry);
        }

        return true;
    }

    /// <summary>
    /// The provider, model and reasoning effort for a run, each falling back to configuration when not given, and
    /// the configured embedding model.
    /// </summary>
    private (string Provider, string Model, string? ReasoningEffort, string EmbeddingModel) Resolve(
        string? provider,
        string? model,
        string? reasoningEffort)
    {
        ClipSummaryOptions current = options.CurrentValue;
        string resolvedProvider = string.IsNullOrWhiteSpace(provider) ? current.Provider : provider.Trim();
        string resolvedModel = string.IsNullOrWhiteSpace(model) ? current.Model : model.Trim();

        if (!summarizerFactory.IsConfigured(resolvedProvider))
        {
            throw new BadRequestException($"Clip summary provider '{resolvedProvider}' is not configured");
        }

        if (string.IsNullOrWhiteSpace(resolvedModel))
        {
            throw new BadRequestException("No clip summary model given and ClipSummary:Model is not set");
        }

        if (!summarizerFactory.IsEmbeddingConfigured)
        {
            throw new BadRequestException(
                "Clip summary embeddings are not configured; set ClipSummary:Providers:openai:ApiKey and ClipSummary:EmbeddingModel");
        }

        string? resolvedEffort;
        try
        {
            resolvedEffort = LegendReasoningEffort.Normalize(
                string.IsNullOrWhiteSpace(reasoningEffort) ? current.ReasoningEffort : reasoningEffort);
        }
        catch (ArgumentException ex)
        {
            throw new BadRequestException(ex.Message);
        }

        return (resolvedProvider.ToLowerInvariant(), resolvedModel, resolvedEffort, current.EmbeddingModel.Trim());
    }
}
