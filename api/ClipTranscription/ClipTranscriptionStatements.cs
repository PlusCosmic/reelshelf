using Dapper;
using Npgsql;

namespace Reelshelf.ClipTranscription;

public class ClipTranscriptionStatements(NpgsqlConnection connection)
{
    public const string AutoTrigger = "auto";
    public const string ManualTrigger = "manual";
    public const string RetryTrigger = "retry";

    private const string RunColumns = """
        id, clip_id, trigger, model, prompt_version, prompt, keywords, status, attempts, has_audio, audio_seconds,
        audio_bytes, transcript, languages, raw_response, input_tokens, output_tokens, duration_ms, error, created_at,
        started_at, completed_at
        """;

    /// <summary>Queues a clip's automatic run. Returns false when the clip already has one.</summary>
    public async Task<bool> QueueAutoRunAsync(Guid clipId, string model)
    {
        const string sql = """
            INSERT INTO clip_transcription_run (clip_id, trigger, model)
            VALUES (@ClipId, 'auto', @Model)
            ON CONFLICT (clip_id) WHERE trigger = 'auto' DO NOTHING
            """;
        return await connection.ExecuteAsync(sql, new { ClipId = clipId, Model = model }) > 0;
    }

    /// <summary>
    /// Queues an automatic run for every encoded clip of the given owners that has never had one. Clips still
    /// encoding are left for their webhook.
    /// </summary>
    public async Task<int> QueueAutoRunsForOwnersAsync(List<Guid> ownerIds, string model)
    {
        if (ownerIds.Count == 0)
        {
            return 0;
        }

        const string sql = """
            INSERT INTO clip_transcription_run (clip_id, trigger, model)
            SELECT id, 'auto', @Model
            FROM clip
            WHERE owner_id = ANY(@OwnerIds) AND video_status IN (3, 4)
            ORDER BY created_at
            ON CONFLICT (clip_id) WHERE trigger = 'auto' DO NOTHING
            """;
        return await connection.ExecuteAsync(sql, new { OwnerIds = ownerIds, Model = model });
    }

    /// <summary>
    /// Queues a <c>retry</c> run with <paramref name="model"/> for each clip whose latest run succeeded with audio
    /// but returned no words, when that run used another model. A clip with a newer run queued is skipped.
    /// </summary>
    public async Task<int> QueueRetriesForEmptyRunsAsync(string model)
    {
        const string sql = """
            INSERT INTO clip_transcription_run (clip_id, trigger, model)
            SELECT latest.clip_id, 'retry', @Model
            FROM (
                SELECT DISTINCT ON (clip_id) clip_id, status, has_audio, transcript, model
                FROM clip_transcription_run
                ORDER BY clip_id, created_at DESC
            ) latest
            WHERE latest.status = 'succeeded'
              AND latest.has_audio
              AND latest.transcript = ''
              AND latest.model <> @Model
            """;
        return await connection.ExecuteAsync(sql, new { Model = model });
    }

    public async Task<ClipTranscriptionRunRow> QueueManualRunAsync(Guid clipId, string model)
    {
        string sql = $"""
            INSERT INTO clip_transcription_run (clip_id, trigger, model)
            VALUES (@ClipId, 'manual', @Model)
            RETURNING {RunColumns}
            """;
        return await connection.QuerySingleAsync<ClipTranscriptionRunRow>(sql, new { ClipId = clipId, Model = model });
    }

    /// <summary>
    /// Claims the next pending run, or a running one whose worker stopped before finishing, and counts the
    /// attempt. Manual and retry runs go before automatic ones, so they never wait behind a backfill; otherwise
    /// the oldest goes first. A run that already failed an attempt waits <paramref name="retryAfter"/> before it
    /// is retried.
    /// SKIP LOCKED keeps two API instances from claiming the same run.
    /// </summary>
    public async Task<ClaimedRunRow?> ClaimNextRunAsync(TimeSpan retryAfter, TimeSpan staleAfter)
    {
        const string sql = """
            UPDATE clip_transcription_run run
            SET status = 'running', attempts = run.attempts + 1, started_at = now()
            FROM clip
            JOIN game_category game ON game.id = clip.game_category_id
            WHERE clip.id = run.clip_id
              AND run.id = (
                  SELECT id FROM clip_transcription_run
                  WHERE (status = 'pending' AND (started_at IS NULL OR started_at < now() - @RetryAfter))
                     OR (status = 'running' AND started_at < now() - @StaleAfter)
                  ORDER BY trigger = 'auto', created_at
                  LIMIT 1
                  FOR UPDATE SKIP LOCKED)
            RETURNING run.id, run.clip_id, clip.video_id, run.trigger, run.model, run.attempts,
                      game.name AS game_name, game.slug AS game_slug
            """;
        return await connection.QuerySingleOrDefaultAsync<ClaimedRunRow>(sql,
            new { RetryAfter = retryAfter, StaleAfter = staleAfter });
    }

    public async Task CompleteRunAsync(
        Guid runId,
        TranscriptionPrompt prompt,
        ClipAudio audio,
        ClipTranscript? transcript,
        int durationMs)
    {
        const string sql = """
            UPDATE clip_transcription_run
            SET status = 'succeeded',
                prompt_version = @PromptVersion,
                prompt = @Prompt,
                keywords = @Keywords,
                has_audio = @HasAudio,
                audio_seconds = @AudioSeconds,
                audio_bytes = @AudioBytes,
                transcript = @Transcript,
                languages = @Languages,
                raw_response = @RawResponse,
                input_tokens = @InputTokens,
                output_tokens = @OutputTokens,
                duration_ms = @DurationMs,
                error = NULL,
                completed_at = now()
            WHERE id = @RunId
            """;
        await connection.ExecuteAsync(sql, new
        {
            RunId = runId,
            PromptVersion = TranscriptionPrompt.Version,
            prompt.Prompt,
            Keywords = prompt.Keywords.ToArray(),
            audio.HasAudio,
            AudioSeconds = (float)audio.Seconds,
            AudioBytes = (int)audio.Bytes,
            Transcript = transcript?.Text ?? "",
            Languages = transcript?.Languages.ToArray() ?? [],
            transcript?.RawResponse,
            transcript?.InputTokens,
            transcript?.OutputTokens,
            DurationMs = durationMs
        });
    }

    /// <summary>Records a failed attempt; the run goes back to pending unless <paramref name="retry"/> is false.</summary>
    public async Task FailRunAsync(Guid runId, string error, TranscriptionPrompt? prompt, bool retry)
    {
        const string sql = """
            UPDATE clip_transcription_run
            SET status = CASE WHEN @Retry THEN 'pending' ELSE 'failed' END,
                error = @Error,
                prompt_version = COALESCE(@PromptVersion, prompt_version),
                prompt = COALESCE(@Prompt, prompt),
                keywords = COALESCE(@Keywords, keywords),
                completed_at = CASE WHEN @Retry THEN NULL ELSE now() END
            WHERE id = @RunId
            """;
        await connection.ExecuteAsync(sql, new
        {
            RunId = runId,
            Error = error,
            PromptVersion = prompt is null ? null : TranscriptionPrompt.Version,
            Prompt = prompt?.Prompt,
            Keywords = prompt?.Keywords.ToArray(),
            Retry = retry
        });
    }

    public async Task<List<ClipTranscriptionRunRow>> GetRunsForClipAsync(Guid clipId)
    {
        string sql = $"""
            SELECT {RunColumns}
            FROM clip_transcription_run
            WHERE clip_id = @ClipId
            ORDER BY created_at DESC
            """;
        return (await connection.QueryAsync<ClipTranscriptionRunRow>(sql, new { ClipId = clipId })).ToList();
    }

    /// <summary>The linked identities of every account that owns a clip, to check against the whitelist.</summary>
    public async Task<List<OwnerIdentityRow>> GetClipOwnerIdentitiesAsync()
    {
        const string sql = """
            SELECT identity.user_id, identity.provider, identity.provider_user_id
            FROM user_identity identity
            WHERE EXISTS (SELECT 1 FROM clip WHERE clip.owner_id = identity.user_id)
            """;
        return (await connection.QueryAsync<OwnerIdentityRow>(sql)).ToList();
    }

    /// <summary>Clips owned by the given accounts, plus any other clip that has been transcribed, newest first.</summary>
    public async Task<List<ReviewClipRow>> GetClipsForReviewAsync(List<Guid> ownerIds)
    {
        const string sql = """
            SELECT clip.id, clip.video_id, clip.title, clip.length, clip.created_at,
                   COALESCE(owner.global_name, owner.username) AS owner_name,
                   game.name AS game_name, game.slug AS game_slug
            FROM clip
            JOIN app_user owner ON owner.id = clip.owner_id
            JOIN game_category game ON game.id = clip.game_category_id
            WHERE clip.owner_id = ANY(@OwnerIds)
               OR EXISTS (SELECT 1 FROM clip_transcription_run run WHERE run.clip_id = clip.id)
            ORDER BY clip.created_at DESC
            """;
        return (await connection.QueryAsync<ReviewClipRow>(sql, new { OwnerIds = ownerIds })).ToList();
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
            FROM clip_transcription_run
            WHERE clip_id = ANY(@ClipIds)
            ORDER BY clip_id, created_at DESC
            """;
        return (await connection.QueryAsync<LatestRunRow>(sql, new { ClipIds = clipIds })).ToList();
    }

    /// <summary>
    /// Run counts, audio minutes and token totals per model and prompt version, newest first. Queued runs and
    /// runs that failed before being sent count towards their model's latest prompt version.
    /// </summary>
    public async Task<List<UsageRow>> GetUsageAsync()
    {
        const string sql = """
            WITH versioned AS (
                SELECT run.*,
                       COALESCE(run.prompt_version, FIRST_VALUE(run.prompt_version) OVER (
                           PARTITION BY run.model
                           ORDER BY run.prompt_version IS NULL, run.created_at DESC)) AS batch_prompt_version
                FROM clip_transcription_run run
            )
            SELECT model,
                   batch_prompt_version AS prompt_version,
                   COUNT(*) FILTER (WHERE status = 'succeeded') AS succeeded,
                   COUNT(*) FILTER (WHERE status = 'failed') AS failed,
                   COUNT(*) FILTER (WHERE status IN ('pending', 'running')) AS queued,
                   COUNT(*) FILTER (WHERE status = 'succeeded' AND has_audio = false) AS without_audio,
                   COUNT(*) FILTER (WHERE status = 'succeeded' AND has_audio AND transcript = '') AS without_speech,
                   COALESCE(SUM(audio_seconds) FILTER (WHERE status = 'succeeded' AND has_audio), 0)::float8
                       AS audio_seconds,
                   COALESCE(SUM(input_tokens), 0) AS input_tokens,
                   COALESCE(SUM(output_tokens), 0) AS output_tokens,
                   AVG(duration_ms)::int AS average_duration_ms
            FROM versioned
            GROUP BY model, batch_prompt_version
            ORDER BY MAX(created_at) DESC
            """;
        return (await connection.QueryAsync<UsageRow>(sql)).ToList();
    }

    public class OwnerIdentityRow
    {
        public Guid UserId { get; set; }
        public string Provider { get; set; } = "";
        public string ProviderUserId { get; set; } = "";
    }

    public class ReviewClipRow
    {
        public Guid Id { get; set; }
        public Guid VideoId { get; set; }
        public string? Title { get; set; }
        public int? Length { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string OwnerName { get; set; } = "";
        public string GameName { get; set; } = "";
        public string GameSlug { get; set; } = "";
    }

    public class LatestRunRow : ClipTranscriptionRunRow
    {
        public long RunCount { get; set; }
    }

    public class UsageRow
    {
        public string Model { get; set; } = "";
        public string? PromptVersion { get; set; }
        public long Succeeded { get; set; }
        public long Failed { get; set; }
        public long Queued { get; set; }
        public long WithoutAudio { get; set; }
        public long WithoutSpeech { get; set; }
        public double AudioSeconds { get; set; }
        public long InputTokens { get; set; }
        public long OutputTokens { get; set; }
        public int? AverageDurationMs { get; set; }
    }

    public class ClaimedRunRow
    {
        public Guid Id { get; set; }
        public Guid ClipId { get; set; }
        public Guid VideoId { get; set; }
        public string Trigger { get; set; } = "";
        public string Model { get; set; } = "";
        public int Attempts { get; set; }
        public string? GameName { get; set; }
        public string? GameSlug { get; set; }
    }

    public class ClipTranscriptionRunRow
    {
        public Guid Id { get; set; }
        public Guid ClipId { get; set; }
        public string Trigger { get; set; } = "";
        public string Model { get; set; } = "";
        public string? PromptVersion { get; set; }
        public string? Prompt { get; set; }
        public string[]? Keywords { get; set; }
        public string Status { get; set; } = "";
        public int Attempts { get; set; }
        public bool? HasAudio { get; set; }
        public float? AudioSeconds { get; set; }
        public int? AudioBytes { get; set; }
        public string? Transcript { get; set; }
        public string[]? Languages { get; set; }
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
