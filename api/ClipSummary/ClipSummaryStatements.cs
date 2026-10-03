using Dapper;
using Npgsql;
using Pgvector;

namespace Reelshelf.ClipSummary;

public class ClipSummaryStatements(NpgsqlConnection connection)
{
    public const string AutoTrigger = "auto";
    public const string ManualTrigger = "manual";
    public const string RetryTrigger = "retry";

    private const string RunColumns = """
        id, clip_id, source_transcription_run_id, trigger, provider, model, reasoning_effort, prompt_version, status,
        attempts, description, mood_tags, quotes::text AS quotes, people, embedding IS NOT NULL AS has_embedding,
        embedding_model, raw_response, input_tokens, cached_input_tokens, output_tokens, embedding_tokens, duration_ms,
        error, created_at, started_at, completed_at
        """;

    /// <summary>
    /// Queues a run summarising the transcript of <paramref name="transcriptionRunId"/>. An automatic run is
    /// skipped when the clip already has one; returns whether a run was queued.
    /// </summary>
    public async Task<bool> QueueRunForTranscriptionAsync(
        Guid transcriptionRunId,
        string trigger,
        string provider,
        string model,
        string? reasoningEffort,
        string embeddingModel)
    {
        const string sql = """
            INSERT INTO clip_summary_run (clip_id, source_transcription_run_id, trigger, provider, model,
                                          reasoning_effort, embedding_model)
            SELECT clip_id, id, @Trigger, @Provider, @Model, @ReasoningEffort, @EmbeddingModel
            FROM clip_transcription_run
            WHERE id = @TranscriptionRunId AND status = 'succeeded'
            ON CONFLICT (clip_id) WHERE trigger = 'auto' DO NOTHING
            """;
        return await connection.ExecuteAsync(sql, new
        {
            TranscriptionRunId = transcriptionRunId,
            Trigger = trigger,
            Provider = provider,
            Model = model,
            ReasoningEffort = reasoningEffort,
            EmbeddingModel = embeddingModel
        }) > 0;
    }

    /// <summary>
    /// Queues an automatic run for every clip with a succeeded transcript that has never had one, summarising its
    /// latest succeeded transcript.
    /// </summary>
    public async Task<int> QueueAutoRunsForTranscribedClipsAsync(
        string provider,
        string model,
        string? reasoningEffort,
        string embeddingModel)
    {
        const string sql = """
            INSERT INTO clip_summary_run (clip_id, source_transcription_run_id, trigger, provider, model,
                                          reasoning_effort, embedding_model)
            SELECT latest.clip_id, latest.id, 'auto', @Provider, @Model, @ReasoningEffort, @EmbeddingModel
            FROM (
                SELECT DISTINCT ON (clip_id) clip_id, id, created_at
                FROM clip_transcription_run
                WHERE status = 'succeeded'
                ORDER BY clip_id, created_at DESC
            ) latest
            ORDER BY latest.created_at
            ON CONFLICT (clip_id) WHERE trigger = 'auto' DO NOTHING
            """;
        return await connection.ExecuteAsync(sql, new
        {
            Provider = provider,
            Model = model,
            ReasoningEffort = reasoningEffort,
            EmbeddingModel = embeddingModel
        });
    }

    /// <summary>Queues a manual run on the clip's latest succeeded transcript, or returns null when it has none.</summary>
    public async Task<ClipSummaryRunRow?> QueueManualRunAsync(
        Guid clipId,
        string provider,
        string model,
        string? reasoningEffort,
        string embeddingModel)
    {
        string sql = $"""
            INSERT INTO clip_summary_run (clip_id, source_transcription_run_id, trigger, provider, model,
                                          reasoning_effort, embedding_model)
            SELECT clip_id, id, 'manual', @Provider, @Model, @ReasoningEffort, @EmbeddingModel
            FROM clip_transcription_run
            WHERE clip_id = @ClipId AND status = 'succeeded'
            ORDER BY created_at DESC
            LIMIT 1
            RETURNING {RunColumns}
            """;
        return await connection.QuerySingleOrDefaultAsync<ClipSummaryRunRow>(sql, new
        {
            ClipId = clipId,
            Provider = provider,
            Model = model,
            ReasoningEffort = reasoningEffort,
            EmbeddingModel = embeddingModel
        });
    }

    /// <summary>
    /// Claims the next pending run, or a running one whose worker stopped before finishing, and counts the
    /// attempt. Manual and retry runs go before automatic ones, so they never wait behind a backfill; otherwise
    /// the oldest goes first. A run that already failed an attempt waits <paramref name="retryAfter"/> before it
    /// is retried. Returns what the summarizer is told about the clip, including the owner's legend from the
    /// latest succeeded legend detection run on Apex clips.
    /// SKIP LOCKED keeps two API instances from claiming the same run.
    /// </summary>
    public async Task<ClaimedRunRow?> ClaimNextRunAsync(TimeSpan retryAfter, TimeSpan staleAfter)
    {
        const string sql = """
            UPDATE clip_summary_run run
            SET status = 'running', attempts = run.attempts + 1, started_at = now()
            FROM clip
            JOIN game_category game ON game.id = clip.game_category_id,
                 clip_transcription_run source
            WHERE clip.id = run.clip_id
              AND source.id = run.source_transcription_run_id
              AND run.id = (
                  SELECT id FROM clip_summary_run
                  WHERE (status = 'pending' AND (started_at IS NULL OR started_at < now() - @RetryAfter))
                     OR (status = 'running' AND started_at < now() - @StaleAfter)
                  ORDER BY trigger = 'auto', created_at
                  LIMIT 1
                  FOR UPDATE SKIP LOCKED)
            RETURNING run.id, run.clip_id, run.trigger, run.provider, run.model, run.reasoning_effort,
                      run.embedding_model, run.attempts, clip.title, clip.created_at AS clip_created_at,
                      game.name AS game_name, source.transcript, source.has_audio,
                      CASE WHEN game.slug = 'apex-legends' THEN (
                          SELECT legend.player_legend
                          FROM legend_detection_run legend
                          WHERE legend.clip_id = clip.id AND legend.status = 'succeeded'
                            AND legend.archived_at IS NULL
                          ORDER BY legend.completed_at DESC
                          LIMIT 1)
                      END AS player_legend
            """;
        return await connection.QuerySingleOrDefaultAsync<ClaimedRunRow>(sql,
            new { RetryAfter = retryAfter, StaleAfter = staleAfter });
    }

    public async Task CompleteRunAsync(
        Guid runId,
        string promptVersion,
        ClipSummarization summarization,
        ClipEmbedding embedding,
        int durationMs)
    {
        const string sql = """
            UPDATE clip_summary_run
            SET status = 'succeeded',
                prompt_version = @PromptVersion,
                description = @Description,
                mood_tags = @MoodTags,
                quotes = @Quotes::jsonb,
                people = @People,
                embedding = @Embedding,
                raw_response = @RawResponse,
                input_tokens = @InputTokens,
                cached_input_tokens = @CachedInputTokens,
                output_tokens = @OutputTokens,
                embedding_tokens = @EmbeddingTokens,
                duration_ms = @DurationMs,
                error = NULL,
                completed_at = now()
            WHERE id = @RunId
            """;
        ClipSummaryResult result = summarization.Result;
        await connection.ExecuteAsync(sql, new
        {
            RunId = runId,
            PromptVersion = promptVersion,
            result.Description,
            MoodTags = result.MoodTags.ToArray(),
            Quotes = result.QuotesJson(),
            People = result.People.ToArray(),
            Embedding = new Vector(embedding.Vector),
            summarization.RawResponse,
            InputTokens = (int?)summarization.InputTokens,
            CachedInputTokens = (int?)summarization.CachedInputTokens,
            OutputTokens = (int?)summarization.OutputTokens,
            EmbeddingTokens = (int?)embedding.InputTokens,
            DurationMs = durationMs
        });
    }

    /// <summary>Records a failed attempt; the run goes back to pending unless <paramref name="retry"/> is false.</summary>
    public async Task FailRunAsync(Guid runId, string error, string? promptVersion, string? rawResponse, bool retry)
    {
        const string sql = """
            UPDATE clip_summary_run
            SET status = CASE WHEN @Retry THEN 'pending' ELSE 'failed' END,
                error = @Error,
                prompt_version = COALESCE(@PromptVersion, prompt_version),
                raw_response = COALESCE(@RawResponse, raw_response),
                completed_at = CASE WHEN @Retry THEN NULL ELSE now() END
            WHERE id = @RunId
            """;
        await connection.ExecuteAsync(sql, new
        {
            RunId = runId,
            Error = error,
            PromptVersion = promptVersion,
            RawResponse = rawResponse,
            Retry = retry
        });
    }

    public async Task<List<ClipSummaryRunRow>> GetRunsForClipAsync(Guid clipId)
    {
        string sql = $"""
            SELECT {RunColumns}
            FROM clip_summary_run
            WHERE clip_id = @ClipId
            ORDER BY created_at DESC
            """;
        return (await connection.QueryAsync<ClipSummaryRunRow>(sql, new { ClipId = clipId })).ToList();
    }

    /// <summary>The most recent run for each clip.</summary>
    public async Task<List<ClipSummaryRunRow>> GetLatestRunsAsync(List<Guid> clipIds)
    {
        if (clipIds.Count == 0)
        {
            return [];
        }

        string sql = $"""
            SELECT DISTINCT ON (clip_id) {RunColumns}
            FROM clip_summary_run
            WHERE clip_id = ANY(@ClipIds)
            ORDER BY clip_id, created_at DESC
            """;
        return (await connection.QueryAsync<ClipSummaryRunRow>(sql, new { ClipIds = clipIds })).ToList();
    }

    /// <summary>
    /// Run counts and token totals per provider, model, reasoning effort and prompt version, newest first. Queued
    /// runs and runs that failed before being sent count towards their model's latest prompt version.
    /// </summary>
    public async Task<List<UsageRow>> GetUsageAsync()
    {
        const string sql = """
            WITH versioned AS (
                SELECT run.*,
                       COALESCE(run.prompt_version, FIRST_VALUE(run.prompt_version) OVER (
                           PARTITION BY run.provider, run.model, run.reasoning_effort
                           ORDER BY run.prompt_version IS NULL, run.created_at DESC)) AS batch_prompt_version
                FROM clip_summary_run run
            )
            SELECT provider,
                   model,
                   reasoning_effort,
                   batch_prompt_version AS prompt_version,
                   COUNT(*) FILTER (WHERE status = 'succeeded') AS succeeded,
                   COUNT(*) FILTER (WHERE status = 'failed') AS failed,
                   COUNT(*) FILTER (WHERE status IN ('pending', 'running')) AS queued,
                   COALESCE(SUM(input_tokens), 0) AS input_tokens,
                   COALESCE(SUM(cached_input_tokens), 0) AS cached_input_tokens,
                   COALESCE(SUM(output_tokens), 0) AS output_tokens,
                   COALESCE(SUM(embedding_tokens), 0) AS embedding_tokens,
                   AVG(duration_ms)::int AS average_duration_ms
            FROM versioned
            GROUP BY provider, model, reasoning_effort, batch_prompt_version
            ORDER BY MAX(created_at) DESC
            """;
        return (await connection.QueryAsync<UsageRow>(sql)).ToList();
    }

    public class UsageRow
    {
        public string Provider { get; set; } = "";
        public string Model { get; set; } = "";
        public string? ReasoningEffort { get; set; }
        public string? PromptVersion { get; set; }
        public long Succeeded { get; set; }
        public long Failed { get; set; }
        public long Queued { get; set; }
        public long InputTokens { get; set; }
        public long CachedInputTokens { get; set; }
        public long OutputTokens { get; set; }
        public long EmbeddingTokens { get; set; }
        public int? AverageDurationMs { get; set; }
    }

    public class ClaimedRunRow
    {
        public Guid Id { get; set; }
        public Guid ClipId { get; set; }
        public string Trigger { get; set; } = "";
        public string Provider { get; set; } = "";
        public string Model { get; set; } = "";
        public string? ReasoningEffort { get; set; }
        public string EmbeddingModel { get; set; } = "";
        public int Attempts { get; set; }
        public string? Title { get; set; }
        public DateTimeOffset ClipCreatedAt { get; set; }
        public string? GameName { get; set; }
        public string? Transcript { get; set; }
        public bool? HasAudio { get; set; }
        public string? PlayerLegend { get; set; }
    }

    public class ClipSummaryRunRow
    {
        public Guid Id { get; set; }
        public Guid ClipId { get; set; }
        public Guid SourceTranscriptionRunId { get; set; }
        public string Trigger { get; set; } = "";
        public string Provider { get; set; } = "";
        public string Model { get; set; } = "";
        public string? ReasoningEffort { get; set; }
        public string? PromptVersion { get; set; }
        public string Status { get; set; } = "";
        public int Attempts { get; set; }
        public string? Description { get; set; }
        public string[]? MoodTags { get; set; }
        public string? Quotes { get; set; }
        public string[]? People { get; set; }
        public bool HasEmbedding { get; set; }
        public string EmbeddingModel { get; set; } = "";
        public string? RawResponse { get; set; }
        public int? InputTokens { get; set; }
        public int? CachedInputTokens { get; set; }
        public int? OutputTokens { get; set; }
        public int? EmbeddingTokens { get; set; }
        public int? DurationMs { get; set; }
        public string? Error { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
    }
}

/// <summary>
/// Lets Dapper pass and read pgvector's <see cref="Vector"/>. Npgsql does the conversion once the data source is
/// built with <c>UseVector()</c>; Dapper only needs to be told to hand the value through.
/// </summary>
public sealed class VectorTypeHandler : SqlMapper.TypeHandler<Vector?>
{
    public override void SetValue(System.Data.IDbDataParameter parameter, Vector? value)
    {
        parameter.Value = value is null ? DBNull.Value : value;
    }

    public override Vector? Parse(object value)
    {
        return (Vector)value;
    }
}
