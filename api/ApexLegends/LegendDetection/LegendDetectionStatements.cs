using Dapper;
using Npgsql;

namespace Reelshelf.ApexLegends.LegendDetection;

public class LegendDetectionStatements(NpgsqlConnection connection)
{
    public const string AutoTrigger = "auto";
    public const string ManualTrigger = "manual";

    /// <summary>A run that belongs to an eval: scored against the baseline and never used as a clip's legend.</summary>
    public const string EvalTrigger = "eval";

    private const string RunColumns = """
        id, clip_id, trigger, provider, model, reasoning_effort, prompt_version, status, attempts, frame_count, hud_detected,
        player_legend, player_legend_confidence, player_name, player_name_confidence, teammates::text AS teammates,
        raw_response, input_tokens, cached_input_tokens, cache_write_input_tokens, output_tokens, cost_usd,
        duration_ms, error, eval_id, created_at, started_at, completed_at
        """;

    /// <summary>Queues a clip's automatic run. Returns false when the clip already has an unarchived one.</summary>
    public async Task<bool> QueueAutoRunAsync(Guid clipId, string provider, string model, string? reasoningEffort)
    {
        const string sql = """
            INSERT INTO legend_detection_run (clip_id, trigger, provider, model, reasoning_effort)
            VALUES (@ClipId, 'auto', @Provider, @Model, @ReasoningEffort)
            ON CONFLICT (clip_id) WHERE trigger = 'auto' AND archived_at IS NULL DO NOTHING
            """;
        return await connection.ExecuteAsync(sql, new
        {
            ClipId = clipId, Provider = provider, Model = model, ReasoningEffort = reasoningEffort
        }) > 0;
    }

    /// <summary>Queues an automatic run for every clip in the category without an unarchived one.</summary>
    public async Task<int> QueueAutoRunsForCategoryAsync(
        Guid gameCategoryId,
        string provider,
        string model,
        string? reasoningEffort)
    {
        const string sql = """
            INSERT INTO legend_detection_run (clip_id, trigger, provider, model, reasoning_effort)
            SELECT id, 'auto', @Provider, @Model, @ReasoningEffort
            FROM clip
            WHERE game_category_id = @GameCategoryId
            ON CONFLICT (clip_id) WHERE trigger = 'auto' AND archived_at IS NULL DO NOTHING
            """;
        return await connection.ExecuteAsync(sql, new
        {
            GameCategoryId = gameCategoryId, Provider = provider, Model = model, ReasoningEffort = reasoningEffort
        });
    }

    public async Task<LegendDetectionRunRow> QueueManualRunAsync(
        Guid clipId,
        string provider,
        string model,
        string? reasoningEffort)
    {
        string sql = $"""
            INSERT INTO legend_detection_run (clip_id, trigger, provider, model, reasoning_effort)
            VALUES (@ClipId, 'manual', @Provider, @Model, @ReasoningEffort)
            RETURNING {RunColumns}
            """;
        return await connection.QuerySingleAsync<LegendDetectionRunRow>(sql, new
        {
            ClipId = clipId, Provider = provider, Model = model, ReasoningEffort = reasoningEffort
        });
    }

    /// <summary>
    /// Creates an eval and queues one of its runs for every labelled clip. Returns the eval's id and how many runs
    /// were queued; no eval is created when no clip is labelled.
    /// </summary>
    public async Task<(Guid? EvalId, int Queued)> CreateEvalAsync(
        string provider,
        string model,
        string? reasoningEffort,
        Guid createdBy)
    {
        // Dapper opens the connection for each query on its own, but a transaction needs it open throughout.
        bool openedConnection = false;
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
            openedConnection = true;
        }

        try
        {
            return await CreateEvalInTransactionAsync(provider, model, reasoningEffort, createdBy);
        }
        finally
        {
            if (openedConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task<(Guid? EvalId, int Queued)> CreateEvalInTransactionAsync(
        string provider,
        string model,
        string? reasoningEffort,
        Guid createdBy)
    {
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
        int labelled = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM legend_detection_label", transaction: transaction);
        if (labelled == 0)
        {
            return (null, 0);
        }

        Guid evalId = await connection.ExecuteScalarAsync<Guid>("""
            INSERT INTO legend_eval (provider, model, reasoning_effort, created_by)
            VALUES (@Provider, @Model, @ReasoningEffort, @CreatedBy)
            RETURNING id
            """, new { Provider = provider, Model = model, ReasoningEffort = reasoningEffort, CreatedBy = createdBy },
            transaction);
        int queued = await connection.ExecuteAsync("""
            INSERT INTO legend_detection_run (clip_id, trigger, provider, model, reasoning_effort, eval_id)
            SELECT clip_id, 'eval', @Provider, @Model, @ReasoningEffort, @EvalId
            FROM legend_detection_label
            """, new { Provider = provider, Model = model, ReasoningEffort = reasoningEffort, EvalId = evalId },
            transaction);
        await transaction.CommitAsync();
        return (evalId, queued);
    }

    /// <summary>Deletes an eval and its runs, including any still queued. Returns false when there was no such eval.</summary>
    public async Task<bool> DeleteEvalAsync(Guid evalId)
    {
        return await connection.ExecuteAsync("DELETE FROM legend_eval WHERE id = @EvalId", new { EvalId = evalId }) > 0;
    }

    /// <summary>
    /// Queues a stronger model's run for a clip whose automatic run was unsure; see
    /// <see cref="LegendDetectionOptions.EscalationModel"/>.
    /// </summary>
    public async Task QueueEscalationRunAsync(Guid clipId, string provider, string model, string? reasoningEffort)
    {
        const string sql = """
            INSERT INTO legend_detection_run (clip_id, trigger, provider, model, reasoning_effort)
            VALUES (@ClipId, 'escalation', @Provider, @Model, @ReasoningEffort)
            """;
        await connection.ExecuteAsync(sql, new
        {
            ClipId = clipId, Provider = provider, Model = model, ReasoningEffort = reasoningEffort
        });
    }

    /// <summary>
    /// Claims the next pending run, or a running one whose worker stopped before finishing, and counts the
    /// attempt. Manual and escalation runs go before automatic ones, so they never wait behind a backfill, and
    /// eval runs go last, so an eval never holds up a new clip; otherwise the oldest goes first. A run that already failed an attempt waits <paramref name="retryAfter"/>
    /// before it is retried. SKIP LOCKED keeps two API instances from claiming the same run.
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
                  ORDER BY trigger = 'eval', trigger = 'auto', created_at
                  LIMIT 1
                  FOR UPDATE SKIP LOCKED)
            RETURNING run.id, run.clip_id, clip.video_id, clip.video_host, run.trigger, run.provider, run.model, run.reasoning_effort,
                      run.attempts
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
                cache_write_input_tokens = @CacheWriteInputTokens,
                output_tokens = @OutputTokens,
                cost_usd = @CostUsd,
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
            PlayerNameConfidence = (float?)result.Player.NameConfidence,
            Teammates = result.TeammatesJson(),
            recognition.RawResponse,
            InputTokens = (int?)recognition.InputTokens,
            CachedInputTokens = (int?)recognition.CachedInputTokens,
            OutputTokens = (int?)recognition.OutputTokens,
            CacheWriteInputTokens = (int?)recognition.CacheWriteInputTokens,
            recognition.CostUsd,
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

    /// <summary>
    /// Archives every unarchived run, including queued ones, which will then never be processed. Eval runs are left
    /// alone: an eval is removed by deleting it.
    /// </summary>
    public async Task<int> ArchiveAllRunsAsync()
    {
        const string sql = """
            UPDATE legend_detection_run SET archived_at = now()
            WHERE archived_at IS NULL AND trigger <> 'eval'
            """;
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

    /// <summary>Every owner's clips in the category, since a backfill runs against all of them.</summary>
    public async Task<List<ReviewClipRow>> GetClipsForReviewAsync(Guid gameCategoryId)
    {
        const string sql = """
            SELECT clip.id, clip.video_id, clip.video_host, clip.title, clip.length, clip.created_at,
                   COALESCE(owner.global_name, owner.username) AS owner_name
            FROM clip
            JOIN app_user owner ON owner.id = clip.owner_id
            WHERE clip.game_category_id = @GameCategoryId
            ORDER BY clip.created_at DESC
            """;
        return (await connection.QueryAsync<ReviewClipRow>(sql, new { GameCategoryId = gameCategoryId })).ToList();
    }

    /// <summary>
    /// The most recent run for each clip, with how many runs the clip has had in total. Eval runs are left out, so
    /// the review list shows what production did.
    /// </summary>
    public async Task<List<LatestRunRow>> GetLatestRunsAsync(List<Guid> clipIds)
    {
        if (clipIds.Count == 0)
        {
            return [];
        }

        string sql = $"""
            SELECT DISTINCT ON (clip_id) {RunColumns}, COUNT(*) OVER (PARTITION BY clip_id) AS run_count
            FROM legend_detection_run
            WHERE clip_id = ANY(@ClipIds) AND archived_at IS NULL AND trigger <> 'eval'
            ORDER BY clip_id, created_at DESC
            """;
        return (await connection.QueryAsync<LatestRunRow>(sql, new { ClipIds = clipIds })).ToList();
    }

    /// <summary>
    /// Run counts, token totals and accuracy per provider, model, reasoning effort and prompt version, with each archived batch
    /// kept apart from the current runs. Current runs come first; archived groups that never ran are left out.
    /// Accuracy covers succeeded runs on labelled clips: the owner's legend must match exactly, and teammates as
    /// an unordered set, scored only for runs whose prompt asked for teammates (their <c>teammates</c> is not
    /// null). A confident mistake is any wrong legend reported at <paramref name="confidentAt"/> or above.
    /// </summary>
    public async Task<List<UsageRow>> GetUsageAsync(double confidentAt)
    {
        const string sql = """
            WITH scored AS (
                SELECT run.*,
                       -- A run has no prompt version until it reaches the model, so queued runs and runs that
                       -- failed before then count towards their batch's latest version instead of a row of their own.
                       COALESCE(run.prompt_version, FIRST_VALUE(run.prompt_version) OVER (
                           PARTITION BY run.provider, run.model, run.reasoning_effort, run.archived_at
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
                   reasoning_effort,
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
                   COUNT(*) FILTER (WHERE labelled AND teammates IS NOT NULL) AS squad_labelled,
                   COUNT(*) FILTER (WHERE labelled AND teammates IS NOT NULL AND teammates_correct) AS teammates_correct,
                   COUNT(*) FILTER (WHERE labelled AND confident_mistake) AS confident_mistakes,
                   archived_at
            FROM scored
            GROUP BY provider, model, reasoning_effort, batch_prompt_version, archived_at
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

    /// <summary>
    /// Sets the owner's legend on many clips at once, keeping any teammate legends a label already has. Ids of
    /// clips that no longer exist are skipped. Returns how many labels were written.
    /// </summary>
    public async Task<int> SetPlayerLabelsAsync(Guid[] clipIds, string?[] playerLegends, Guid labelledBy)
    {
        const string sql = """
            INSERT INTO legend_detection_label (clip_id, player_legend, labelled_by)
            SELECT input.clip_id, input.player_legend, @LabelledBy
            FROM unnest(@ClipIds::uuid[], @PlayerLegends::text[]) AS input(clip_id, player_legend)
            JOIN clip ON clip.id = input.clip_id
            ON CONFLICT (clip_id) DO UPDATE
            SET player_legend = EXCLUDED.player_legend,
                labelled_by = EXCLUDED.labelled_by,
                labelled_at = now()
            """;
        return await connection.ExecuteAsync(sql, new
        {
            ClipIds = clipIds,
            PlayerLegends = playerLegends,
            LabelledBy = labelledBy
        });
    }

    /// <summary>
    /// Each clip's latest succeeded run outside evals, which is the legend the rest of Reelshelf uses for it, such
    /// as in clip summaries.
    /// </summary>
    public async Task<List<LegendDetectionRunRow>> GetCurrentResultsAsync(List<Guid> clipIds)
    {
        if (clipIds.Count == 0)
        {
            return [];
        }

        string sql = $"""
            SELECT DISTINCT ON (clip_id) {RunColumns}
            FROM legend_detection_run
            WHERE clip_id = ANY(@ClipIds) AND archived_at IS NULL AND status = 'succeeded' AND trigger <> 'eval'
            ORDER BY clip_id, completed_at DESC
            """;
        return (await connection.QueryAsync<LegendDetectionRunRow>(sql, new { ClipIds = clipIds })).ToList();
    }

    public async Task<int> DeleteLabelsAsync(Guid[] clipIds)
    {
        return await connection.ExecuteAsync(
            "DELETE FROM legend_detection_label WHERE clip_id = ANY(@ClipIds)", new { ClipIds = clipIds });
    }

    /// <summary>
    /// Each run's cost: what the provider billed, else its tokens at the model's prices, else null when the model
    /// has no input or output price. Cached and cache-write tokens are part of input_tokens and fall back to the
    /// input price.
    /// </summary>
    private const string RunCostSql = """
        COALESCE(run.cost_usd, (
            GREATEST(COALESCE(run.input_tokens, 0) - COALESCE(run.cached_input_tokens, 0)
                     - COALESCE(run.cache_write_input_tokens, 0), 0) * price.input_per_mtok
            + COALESCE(run.cached_input_tokens, 0) * COALESCE(price.cached_input_per_mtok, price.input_per_mtok)
            + COALESCE(run.cache_write_input_tokens, 0) * COALESCE(price.cache_write_per_mtok, price.input_per_mtok)
            + COALESCE(run.output_tokens, 0) * price.output_per_mtok) / 1000000)
        """;

    /// <summary>
    /// Every eval with its results, newest first. A run is scored when it has finished on a clip that is still
    /// labelled: it is correct when its owner's legend matches the label, and a failed run counts as wrong.
    /// Scoring reads the labels as they are now, so correcting the baseline re-scores every eval. Latency covers
    /// the model call of succeeded runs only.
    /// </summary>
    public async Task<List<EvalRow>> GetEvalsAsync(double confidentAt)
    {
        string sql = $$"""
            WITH scored AS (
                SELECT run.*,
                       label.clip_id IS NOT NULL AND run.status IN ('succeeded', 'failed') AS scored,
                       label.clip_id IS NOT NULL AND run.status = 'succeeded'
                           AND run.player_legend IS NOT DISTINCT FROM label.player_legend AS correct,
                       label.clip_id IS NOT NULL AND run.status = 'succeeded'
                           AND run.player_legend IS DISTINCT FROM label.player_legend
                           AND run.player_legend IS NOT NULL
                           AND run.player_legend_confidence >= @ConfidentAt AS confident_mistake,
                       {{RunCostSql}} AS cost
                FROM legend_detection_run run
                LEFT JOIN legend_detection_label label ON label.clip_id = run.clip_id
                LEFT JOIN legend_model_price price ON price.provider = run.provider AND price.model = run.model
                WHERE run.eval_id IS NOT NULL
            )
            SELECT eval.id, eval.provider, eval.model, eval.reasoning_effort, eval.created_at,
                   COUNT(run.id) AS runs,
                   COUNT(*) FILTER (WHERE run.status = 'succeeded') AS succeeded,
                   COUNT(*) FILTER (WHERE run.status = 'failed') AS failed,
                   COUNT(*) FILTER (WHERE run.status IN ('pending', 'running')) AS queued,
                   COUNT(*) FILTER (WHERE run.scored) AS scored,
                   COUNT(*) FILTER (WHERE run.correct) AS correct,
                   COUNT(*) FILTER (WHERE run.confident_mistake) AS confident_mistakes,
                   COALESCE(SUM(run.input_tokens), 0) AS input_tokens,
                   COALESCE(SUM(run.cached_input_tokens), 0) AS cached_input_tokens,
                   COALESCE(SUM(run.cache_write_input_tokens), 0) AS cache_write_input_tokens,
                   COALESCE(SUM(run.output_tokens), 0) AS output_tokens,
                   SUM(run.cost) AS cost_usd,
                   COUNT(*) FILTER (WHERE run.status = 'succeeded' AND run.cost IS NULL) AS unpriced,
                   COUNT(*) FILTER (WHERE run.cost_usd IS NOT NULL) AS billed,
                   (PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY run.duration_ms)
                       FILTER (WHERE run.status = 'succeeded'))::int AS p50_duration_ms,
                   (PERCENTILE_CONT(0.95) WITHIN GROUP (ORDER BY run.duration_ms)
                       FILTER (WHERE run.status = 'succeeded'))::int AS p95_duration_ms,
                   MIN(run.started_at) AS first_started_at,
                   MAX(run.completed_at) AS last_completed_at,
                   COALESCE(ARRAY_AGG(DISTINCT run.prompt_version) FILTER (WHERE run.prompt_version IS NOT NULL),
                            '{}') AS prompt_versions
            FROM legend_eval eval
            LEFT JOIN scored run ON run.eval_id = eval.id
            GROUP BY eval.id
            ORDER BY eval.created_at DESC
            """;
        return (await connection.QueryAsync<EvalRow>(sql, new { ConfidentAt = confidentAt })).ToList();
    }

    /// <summary>
    /// Two evals' finished runs side by side on every labelled clip both have answered, with each run's cost, so a
    /// cascade (the first model, escalating unsure answers to the second) can be worked out without new runs.
    /// </summary>
    public async Task<List<CascadeRow>> GetCascadeRowsAsync(Guid firstEvalId, Guid escalationEvalId)
    {
        string sql = $$"""
            WITH first_run AS (
                SELECT run.clip_id, run.status, run.player_legend, run.player_legend_confidence, run.hud_detected,
                       run.duration_ms, {{RunCostSql}} AS cost
                FROM legend_detection_run run
                LEFT JOIN legend_model_price price ON price.provider = run.provider AND price.model = run.model
                WHERE run.eval_id = @FirstEvalId AND run.status IN ('succeeded', 'failed')
            ),
            escalation_run AS (
                SELECT run.clip_id, run.status, run.player_legend, run.duration_ms, {{RunCostSql}} AS cost
                FROM legend_detection_run run
                LEFT JOIN legend_model_price price ON price.provider = run.provider AND price.model = run.model
                WHERE run.eval_id = @EscalationEvalId AND run.status IN ('succeeded', 'failed')
            )
            SELECT label.clip_id, label.player_legend AS label_legend,
                   first_run.status AS first_status, first_run.player_legend AS first_legend,
                   first_run.player_legend_confidence AS first_confidence,
                   first_run.hud_detected AS first_hud_detected, first_run.duration_ms AS first_duration_ms,
                   first_run.cost AS first_cost_usd,
                   escalation_run.status AS escalation_status, escalation_run.player_legend AS escalation_legend,
                   escalation_run.duration_ms AS escalation_duration_ms, escalation_run.cost AS escalation_cost_usd
            FROM legend_detection_label label
            JOIN first_run ON first_run.clip_id = label.clip_id
            JOIN escalation_run ON escalation_run.clip_id = label.clip_id
            """;
        return (await connection.QueryAsync<CascadeRow>(sql,
            new { FirstEvalId = firstEvalId, EscalationEvalId = escalationEvalId })).ToList();
    }

    /// <summary>An eval's wrong answers on clips that are labelled: failed runs and runs whose legend is not the label.</summary>
    public async Task<List<EvalMissRow>> GetEvalMissesAsync(Guid evalId)
    {
        const string sql = """
            SELECT run.id AS run_id, run.clip_id, clip.video_id, clip.video_host, clip.title, run.status, run.hud_detected,
                   run.player_legend, run.player_legend_confidence, run.error, label.player_legend AS label_legend
            FROM legend_detection_run run
            JOIN legend_detection_label label ON label.clip_id = run.clip_id
            JOIN clip ON clip.id = run.clip_id
            WHERE run.eval_id = @EvalId
              AND (run.status = 'failed'
                   OR (run.status = 'succeeded' AND run.player_legend IS DISTINCT FROM label.player_legend))
            ORDER BY run.status = 'failed', run.player_legend_confidence DESC NULLS LAST
            """;
        return (await connection.QueryAsync<EvalMissRow>(sql, new { EvalId = evalId })).ToList();
    }

    public async Task<bool> EvalExistsAsync(Guid evalId)
    {
        return await connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM legend_eval WHERE id = @EvalId)", new { EvalId = evalId });
    }

    /// <summary>
    /// The prices set so far, plus an empty entry for each provider and model an eval has used without one, so the
    /// page can ask for it. Models whose every finished run was billed by the provider need no price and are left
    /// out unless one was set.
    /// </summary>
    public async Task<List<PriceRow>> GetPricesAsync()
    {
        const string sql = """
            SELECT price.provider, price.model, price.input_per_mtok, price.cached_input_per_mtok,
                   price.cache_write_per_mtok, price.output_per_mtok
            FROM legend_model_price price
            UNION ALL
            SELECT DISTINCT eval.provider, eval.model, NULL::numeric, NULL::numeric, NULL::numeric, NULL::numeric
            FROM legend_eval eval
            WHERE NOT EXISTS (SELECT 1 FROM legend_model_price price
                              WHERE price.provider = eval.provider AND price.model = eval.model)
              AND EXISTS (SELECT 1 FROM legend_detection_run run
                          WHERE run.eval_id = eval.id AND run.status = 'succeeded' AND run.cost_usd IS NULL)
            ORDER BY provider, model
            """;
        return (await connection.QueryAsync<PriceRow>(sql)).ToList();
    }

    public async Task SetPriceAsync(PriceRow price)
    {
        const string sql = """
            INSERT INTO legend_model_price (provider, model, input_per_mtok, cached_input_per_mtok,
                                            cache_write_per_mtok, output_per_mtok)
            VALUES (@Provider, @Model, @InputPerMtok, @CachedInputPerMtok, @CacheWritePerMtok, @OutputPerMtok)
            ON CONFLICT (provider, model) DO UPDATE
            SET input_per_mtok = EXCLUDED.input_per_mtok,
                cached_input_per_mtok = EXCLUDED.cached_input_per_mtok,
                cache_write_per_mtok = EXCLUDED.cache_write_per_mtok,
                output_per_mtok = EXCLUDED.output_per_mtok,
                updated_at = now()
            """;
        await connection.ExecuteAsync(sql, price);
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

    public class EvalRow
    {
        public Guid Id { get; set; }
        public string Provider { get; set; } = "";
        public string Model { get; set; } = "";
        public string? ReasoningEffort { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public long Runs { get; set; }
        public long Succeeded { get; set; }
        public long Failed { get; set; }
        public long Queued { get; set; }
        public long Scored { get; set; }
        public long Correct { get; set; }
        public long ConfidentMistakes { get; set; }
        public long InputTokens { get; set; }
        public long CachedInputTokens { get; set; }
        public long CacheWriteInputTokens { get; set; }
        public long OutputTokens { get; set; }
        public decimal? CostUsd { get; set; }
        public long Unpriced { get; set; }
        public long Billed { get; set; }
        public int? P50DurationMs { get; set; }
        public int? P95DurationMs { get; set; }
        public DateTimeOffset? FirstStartedAt { get; set; }
        public DateTimeOffset? LastCompletedAt { get; set; }
        public string[] PromptVersions { get; set; } = [];
    }

    public class CascadeRow
    {
        public Guid ClipId { get; set; }
        public string? LabelLegend { get; set; }
        public string FirstStatus { get; set; } = "";
        public string? FirstLegend { get; set; }
        public float? FirstConfidence { get; set; }
        public bool? FirstHudDetected { get; set; }
        public int? FirstDurationMs { get; set; }
        public decimal? FirstCostUsd { get; set; }
        public string EscalationStatus { get; set; } = "";
        public string? EscalationLegend { get; set; }
        public int? EscalationDurationMs { get; set; }
        public decimal? EscalationCostUsd { get; set; }
    }

    public class EvalMissRow
    {
        public Guid RunId { get; set; }
        public Guid ClipId { get; set; }
        public Guid VideoId { get; set; }
        public string VideoHost { get; set; } = "";
        public string? Title { get; set; }
        public string Status { get; set; } = "";
        public bool? HudDetected { get; set; }
        public string? PlayerLegend { get; set; }
        public float? PlayerLegendConfidence { get; set; }
        public string? Error { get; set; }
        public string? LabelLegend { get; set; }
    }

    public class PriceRow
    {
        public string Provider { get; set; } = "";
        public string Model { get; set; } = "";
        public decimal? InputPerMtok { get; set; }
        public decimal? CachedInputPerMtok { get; set; }
        public decimal? CacheWritePerMtok { get; set; }
        public decimal? OutputPerMtok { get; set; }
    }

    public class ReviewClipRow
    {
        public Guid Id { get; set; }
        public Guid VideoId { get; set; }
        public string VideoHost { get; set; } = "";
        public string? Title { get; set; }
        public int? Length { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string OwnerName { get; set; } = "";
    }

    public class LatestRunRow : LegendDetectionRunRow
    {
        public long RunCount { get; set; }
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
        public int? AverageDurationMs { get; set; }
        public long Labelled { get; set; }
        public long PlayerCorrect { get; set; }
        public long SquadLabelled { get; set; }
        public long TeammatesCorrect { get; set; }
        public long ConfidentMistakes { get; set; }
        public DateTimeOffset? ArchivedAt { get; set; }
    }

    public class ClaimedRunRow
    {
        public Guid Id { get; set; }
        public Guid ClipId { get; set; }
        public Guid VideoId { get; set; }
        public string VideoHost { get; set; } = "";
        public string Trigger { get; set; } = "";
        public string Provider { get; set; } = "";
        public string Model { get; set; } = "";
        public string? ReasoningEffort { get; set; }
        public int Attempts { get; set; }
    }

    public class LegendDetectionRunRow
    {
        public Guid Id { get; set; }
        public Guid ClipId { get; set; }
        public string Trigger { get; set; } = "";
        public string Provider { get; set; } = "";
        public string Model { get; set; } = "";
        public string? ReasoningEffort { get; set; }
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
        public int? CacheWriteInputTokens { get; set; }
        public int? OutputTokens { get; set; }
        public decimal? CostUsd { get; set; }
        public int? DurationMs { get; set; }
        public string? Error { get; set; }
        public Guid? EvalId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
    }
}
