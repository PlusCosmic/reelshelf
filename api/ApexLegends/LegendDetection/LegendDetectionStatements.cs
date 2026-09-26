using Dapper;
using Npgsql;

namespace Reelshelf.ApexLegends.LegendDetection;

public class LegendDetectionStatements(NpgsqlConnection connection)
{
    public const string AutoTrigger = "auto";
    public const string ManualTrigger = "manual";

    private const string RunColumns = """
        id, clip_id, trigger, provider, model, prompt_version, status, attempts, frame_count, hud_detected,
        player_legend, player_legend_confidence, player_name, player_name_confidence, teammates::text AS teammates,
        raw_response, input_tokens, cached_input_tokens, output_tokens, duration_ms, error, created_at, started_at,
        completed_at
        """;

    /// <summary>Queues a clip's automatic run. Returns false when the clip already has an unarchived one.</summary>
    public async Task<bool> QueueAutoRunAsync(Guid clipId, string provider, string model)
    {
        const string sql = """
            INSERT INTO legend_detection_run (clip_id, trigger, provider, model)
            VALUES (@ClipId, 'auto', @Provider, @Model)
            ON CONFLICT (clip_id) WHERE trigger = 'auto' AND archived_at IS NULL DO NOTHING
            """;
        return await connection.ExecuteAsync(sql, new { ClipId = clipId, Provider = provider, Model = model }) > 0;
    }

    /// <summary>Queues an automatic run for every clip in the category without an unarchived one.</summary>
    public async Task<int> QueueAutoRunsForCategoryAsync(Guid gameCategoryId, string provider, string model)
    {
        const string sql = """
            INSERT INTO legend_detection_run (clip_id, trigger, provider, model)
            SELECT id, 'auto', @Provider, @Model
            FROM clip
            WHERE game_category_id = @GameCategoryId
            ON CONFLICT (clip_id) WHERE trigger = 'auto' AND archived_at IS NULL DO NOTHING
            """;
        return await connection.ExecuteAsync(sql,
            new { GameCategoryId = gameCategoryId, Provider = provider, Model = model });
    }

    public async Task<LegendDetectionRunRow> QueueManualRunAsync(Guid clipId, string provider, string model)
    {
        string sql = $"""
            INSERT INTO legend_detection_run (clip_id, trigger, provider, model)
            VALUES (@ClipId, 'manual', @Provider, @Model)
            RETURNING {RunColumns}
            """;
        return await connection.QuerySingleAsync<LegendDetectionRunRow>(sql,
            new { ClipId = clipId, Provider = provider, Model = model });
    }

    /// <summary>
    /// Claims the oldest pending run, or a running one whose worker stopped before finishing, and counts the
    /// attempt. A run that already failed an attempt waits <paramref name="retryAfter"/> before it is retried.
    /// SKIP LOCKED keeps two API instances from claiming the same run.
    /// </summary>
    public async Task<ClaimedRunRow?> ClaimNextRunAsync(TimeSpan retryAfter, TimeSpan staleAfter)
    {
        const string sql = """
            UPDATE legend_detection_run run
            SET status = 'running', attempts = run.attempts + 1, started_at = now()
            FROM clip
            WHERE clip.id = run.clip_id
              AND run.id = (
                  SELECT id FROM legend_detection_run
                  WHERE archived_at IS NULL
                    AND ((status = 'pending' AND (started_at IS NULL OR started_at < now() - @RetryAfter))
                      OR (status = 'running' AND started_at < now() - @StaleAfter))
                  ORDER BY created_at
                  LIMIT 1
                  FOR UPDATE SKIP LOCKED)
            RETURNING run.id, run.clip_id, clip.video_id, run.provider, run.model, run.attempts
            """;
        return await connection.QuerySingleOrDefaultAsync<ClaimedRunRow>(sql, new { RetryAfter = retryAfter, StaleAfter = staleAfter });
    }

    public async Task CompleteRunAsync(
        Guid runId,
        string promptVersion,
        int frameCount,
        LegendRecognition recognition,
        int durationMs)
    {
        const string sql = """
            UPDATE legend_detection_run
            SET status = 'succeeded',
                prompt_version = @PromptVersion,
                frame_count = @FrameCount,
                hud_detected = @HudDetected,
                player_legend = @PlayerLegend,
                player_legend_confidence = @PlayerLegendConfidence,
                player_name = @PlayerName,
                player_name_confidence = @PlayerNameConfidence,
                teammates = @Teammates::jsonb,
                raw_response = @RawResponse,
                input_tokens = @InputTokens,
                cached_input_tokens = @CachedInputTokens,
                output_tokens = @OutputTokens,
                duration_ms = @DurationMs,
                error = NULL,
                completed_at = now()
            WHERE id = @RunId
            """;
        LegendDetectionResult result = recognition.Result;
        await connection.ExecuteAsync(sql, new
        {
            RunId = runId,
            PromptVersion = promptVersion,
            FrameCount = frameCount,
            result.HudDetected,
            PlayerLegend = result.Player.Legend,
            PlayerLegendConfidence = (float)result.Player.LegendConfidence,
            PlayerName = result.Player.Name,
            PlayerNameConfidence = (float)result.Player.NameConfidence,
            Teammates = result.TeammatesJson(),
            recognition.RawResponse,
            InputTokens = (int?)recognition.InputTokens,
            CachedInputTokens = (int?)recognition.CachedInputTokens,
            OutputTokens = (int?)recognition.OutputTokens,
            DurationMs = durationMs
        });
    }

    /// <summary>Records a failed attempt; the run goes back to pending unless <paramref name="retry"/> is false.</summary>
    public async Task FailRunAsync(Guid runId, string error, string? promptVersion, string? rawResponse, bool retry)
    {
        const string sql = """
            UPDATE legend_detection_run
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

    /// <summary>Archives every unarchived run, including queued ones, which will then never be processed.</summary>
    public async Task<int> ArchiveAllRunsAsync()
    {
        const string sql = "UPDATE legend_detection_run SET archived_at = now() WHERE archived_at IS NULL";
        return await connection.ExecuteAsync(sql);
    }

    public async Task<List<LegendDetectionRunRow>> GetRunsForClipAsync(Guid clipId)
    {
        string sql = $"""
            SELECT {RunColumns}
            FROM legend_detection_run
            WHERE clip_id = @ClipId AND archived_at IS NULL
            ORDER BY created_at DESC
            """;
        return (await connection.QueryAsync<LegendDetectionRunRow>(sql, new { ClipId = clipId })).ToList();
    }

    public async Task<List<ReviewClipRow>> GetClipsForReviewAsync(Guid gameCategoryId, Guid ownerId)
    {
        const string sql = """
            SELECT id, video_id, title, length, created_at
            FROM clip
            WHERE game_category_id = @GameCategoryId AND owner_id = @OwnerId
            ORDER BY created_at DESC
            """;
        return (await connection.QueryAsync<ReviewClipRow>(sql,
            new { GameCategoryId = gameCategoryId, OwnerId = ownerId })).ToList();
    }

    /// <summary>The most recent run for each clip, with how many runs the clip has had in total.</summary>
    public async Task<List<LatestRunRow>> GetLatestRunsAsync(List<Guid> clipIds)
    {
        if (clipIds.Count == 0)
        {
            return [];
        }

        string sql = $"""
            SELECT DISTINCT ON (clip_id) {RunColumns}, COUNT(*) OVER (PARTITION BY clip_id) AS run_count
            FROM legend_detection_run
            WHERE clip_id = ANY(@ClipIds) AND archived_at IS NULL
            ORDER BY clip_id, created_at DESC
            """;
        return (await connection.QueryAsync<LatestRunRow>(sql, new { ClipIds = clipIds })).ToList();
    }

    /// <summary>
    /// Run counts, token totals and accuracy per provider, model and prompt version, with each archived batch
    /// kept apart from the current runs. Current runs come first; archived groups that never ran are left out.
    /// Accuracy covers succeeded runs on labelled clips: the owner's legend must match exactly, and teammates as
    /// an unordered set. A confident mistake is any wrong legend reported at <paramref name="confidentAt"/> or above.
    /// </summary>
    public async Task<List<UsageRow>> GetUsageAsync(double confidentAt)
    {
        const string sql = """
            WITH scored AS (
                SELECT run.*,
                       -- A run has no prompt version until it reaches the model, so queued runs and runs that
                       -- failed before then count towards their batch's latest version instead of a row of their own.
                       COALESCE(run.prompt_version, FIRST_VALUE(run.prompt_version) OVER (
                           PARTITION BY run.provider, run.model, run.archived_at
                           ORDER BY run.prompt_version IS NULL, run.created_at DESC)) AS batch_prompt_version,
                       run.status = 'succeeded' AND label.clip_id IS NOT NULL AS labelled,
                       run.player_legend IS NOT DISTINCT FROM label.player_legend AS player_correct,
                       ARRAY(SELECT teammate ->> 'legend'
                             FROM jsonb_array_elements(COALESCE(run.teammates, '[]'::jsonb)) teammate
                             WHERE teammate ->> 'legend' IS NOT NULL
                             ORDER BY 1)
                           = ARRAY(SELECT legend FROM unnest(label.teammate_legends) legend ORDER BY 1)
                           AS teammates_correct,
                       (run.player_legend IS DISTINCT FROM label.player_legend
                            AND run.player_legend IS NOT NULL
                            AND run.player_legend_confidence >= @ConfidentAt)
                           OR EXISTS (
                               SELECT 1
                               FROM jsonb_array_elements(COALESCE(run.teammates, '[]'::jsonb)) teammate
                               WHERE teammate ->> 'legend' IS NOT NULL
                                 AND (teammate ->> 'legend_confidence')::float8 >= @ConfidentAt
                                 AND NOT (teammate ->> 'legend' = ANY(label.teammate_legends)))
                           AS confident_mistake
                FROM legend_detection_run run
                LEFT JOIN legend_detection_label label ON label.clip_id = run.clip_id
            )
            SELECT provider,
                   model,
                   batch_prompt_version AS prompt_version,
                   COUNT(*) FILTER (WHERE status = 'succeeded') AS succeeded,
                   COUNT(*) FILTER (WHERE status = 'failed') AS failed,
                   COUNT(*) FILTER (WHERE status IN ('pending', 'running') AND archived_at IS NULL) AS queued,
                   COALESCE(SUM(input_tokens), 0) AS input_tokens,
                   COALESCE(SUM(cached_input_tokens), 0) AS cached_input_tokens,
                   COALESCE(SUM(output_tokens), 0) AS output_tokens,
                   AVG(duration_ms)::int AS average_duration_ms,
                   COUNT(*) FILTER (WHERE labelled) AS labelled,
                   COUNT(*) FILTER (WHERE labelled AND player_correct) AS player_correct,
                   COUNT(*) FILTER (WHERE labelled AND teammates_correct) AS teammates_correct,
                   COUNT(*) FILTER (WHERE labelled AND confident_mistake) AS confident_mistakes,
                   archived_at
            FROM scored
            GROUP BY provider, model, batch_prompt_version, archived_at
            HAVING archived_at IS NULL OR COUNT(*) FILTER (WHERE status IN ('succeeded', 'failed')) > 0
            ORDER BY archived_at DESC NULLS FIRST, MAX(created_at) DESC
            """;
        return (await connection.QueryAsync<UsageRow>(sql, new { ConfidentAt = confidentAt })).ToList();
    }

    public async Task SetLabelAsync(Guid clipId, string? playerLegend, string[] teammateLegends, Guid labelledBy)
    {
        const string sql = """
            INSERT INTO legend_detection_label (clip_id, player_legend, teammate_legends, labelled_by)
            VALUES (@ClipId, @PlayerLegend, @TeammateLegends, @LabelledBy)
            ON CONFLICT (clip_id) DO UPDATE
            SET player_legend = EXCLUDED.player_legend,
                teammate_legends = EXCLUDED.teammate_legends,
                labelled_by = EXCLUDED.labelled_by,
                labelled_at = now()
            """;
        await connection.ExecuteAsync(sql, new
        {
            ClipId = clipId,
            PlayerLegend = playerLegend,
            TeammateLegends = teammateLegends,
            LabelledBy = labelledBy
        });
    }

    public async Task DeleteLabelAsync(Guid clipId)
    {
        await connection.ExecuteAsync("DELETE FROM legend_detection_label WHERE clip_id = @ClipId", new { ClipId = clipId });
    }

    public async Task<List<LabelRow>> GetLabelsAsync(List<Guid> clipIds)
    {
        if (clipIds.Count == 0)
        {
            return [];
        }

        const string sql = """
            SELECT clip_id, player_legend, teammate_legends, labelled_at
            FROM legend_detection_label
            WHERE clip_id = ANY(@ClipIds)
            """;
        return (await connection.QueryAsync<LabelRow>(sql, new { ClipIds = clipIds })).ToList();
    }

    public class LabelRow
    {
        public Guid ClipId { get; set; }
        public string? PlayerLegend { get; set; }
        public string[] TeammateLegends { get; set; } = [];
        public DateTimeOffset LabelledAt { get; set; }
    }

    public class ReviewClipRow
    {
        public Guid Id { get; set; }
        public Guid VideoId { get; set; }
        public string? Title { get; set; }
        public int? Length { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    public class LatestRunRow : LegendDetectionRunRow
    {
        public long RunCount { get; set; }
    }

    public class UsageRow
    {
        public string Provider { get; set; } = "";
        public string Model { get; set; } = "";
        public string? PromptVersion { get; set; }
        public long Succeeded { get; set; }
        public long Failed { get; set; }
        public long Queued { get; set; }
        public long InputTokens { get; set; }
        public long CachedInputTokens { get; set; }
        public long OutputTokens { get; set; }
        public int? AverageDurationMs { get; set; }
        public long Labelled { get; set; }
        public long PlayerCorrect { get; set; }
        public long TeammatesCorrect { get; set; }
        public long ConfidentMistakes { get; set; }
        public DateTimeOffset? ArchivedAt { get; set; }
    }

    public class ClaimedRunRow
    {
        public Guid Id { get; set; }
        public Guid ClipId { get; set; }
        public Guid VideoId { get; set; }
        public string Provider { get; set; } = "";
        public string Model { get; set; } = "";
        public int Attempts { get; set; }
    }

    public class LegendDetectionRunRow
    {
        public Guid Id { get; set; }
        public Guid ClipId { get; set; }
        public string Trigger { get; set; } = "";
        public string Provider { get; set; } = "";
        public string Model { get; set; } = "";
        public string? PromptVersion { get; set; }
        public string Status { get; set; } = "";
        public int Attempts { get; set; }
        public int? FrameCount { get; set; }
        public bool? HudDetected { get; set; }
        public string? PlayerLegend { get; set; }
        public float? PlayerLegendConfidence { get; set; }
        public string? PlayerName { get; set; }
        public float? PlayerNameConfidence { get; set; }
        public string? Teammates { get; set; }
        public string? RawResponse { get; set; }
        public int? InputTokens { get; set; }
        public int? CachedInputTokens { get; set; }
        public int? OutputTokens { get; set; }
        public int? DurationMs { get; set; }
        public string? Error { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
    }
}
