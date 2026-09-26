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
        raw_response, input_tokens, output_tokens, duration_ms, error, created_at, started_at, completed_at
        """;

    /// <summary>Queues a clip's automatic run. Returns false when the clip already has one.</summary>
    public async Task<bool> QueueAutoRunAsync(Guid clipId, string provider, string model)
    {
        const string sql = """
            INSERT INTO legend_detection_run (clip_id, trigger, provider, model)
            VALUES (@ClipId, 'auto', @Provider, @Model)
            ON CONFLICT (clip_id) WHERE trigger = 'auto' DO NOTHING
            """;
        return await connection.ExecuteAsync(sql, new { ClipId = clipId, Provider = provider, Model = model }) > 0;
    }

    /// <summary>Queues an automatic run for every clip in the category that has never had one.</summary>
    public async Task<int> QueueAutoRunsForCategoryAsync(Guid gameCategoryId, string provider, string model)
    {
        const string sql = """
            INSERT INTO legend_detection_run (clip_id, trigger, provider, model)
            SELECT id, 'auto', @Provider, @Model
            FROM clip
            WHERE game_category_id = @GameCategoryId
            ON CONFLICT (clip_id) WHERE trigger = 'auto' DO NOTHING
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
                  WHERE (status = 'pending' AND (started_at IS NULL OR started_at < now() - @RetryAfter))
                     OR (status = 'running' AND started_at < now() - @StaleAfter)
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

    public async Task<List<LegendDetectionRunRow>> GetRunsForClipAsync(Guid clipId)
    {
        string sql = $"""
            SELECT {RunColumns}
            FROM legend_detection_run
            WHERE clip_id = @ClipId
            ORDER BY created_at DESC
            """;
        return (await connection.QueryAsync<LegendDetectionRunRow>(sql, new { ClipId = clipId })).ToList();
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
        public int? OutputTokens { get; set; }
        public int? DurationMs { get; set; }
        public string? Error { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
    }
}
