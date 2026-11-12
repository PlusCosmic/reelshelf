using Dapper;
using Npgsql;
using Reelshelf.ApexLegends.LegendDetection;
using Reelshelf.Core;

namespace Reelshelf.Ops;

/// <summary>
/// The read-only aggregates behind <c>/api/admin/ops/</c>. Queue counts come from the queue metrics' own sources;
/// this is the rest: storage, the latest failures, the AAC backfill's remainder and AI spend.
/// </summary>
public sealed class OpsStatements(NpgsqlDataSource dataSource)
{
    /// <summary>How many of the latest failures each list carries.</summary>
    public const int FailureLimit = 50;

    /// <summary>How many accounts the top accounts by bytes list.</summary>
    public const int TopAccountLimit = 20;

    public static readonly IReadOnlyList<string> TierNames = ["free", "plus", "pro", "unlimited"];

    /// <summary>
    /// Each account's tier now, as <c>AccountTiers.Resolve</c> works it out: the better of its granted tier and the best
    /// tier among its subscriptions that are active, trialing or past due, else free.
    /// </summary>
    private const string AccountTierSql = """
        SELECT app_user.id, app_user.username,
               (ARRAY['free', 'plus', 'pro', 'unlimited'])[1 + GREATEST(
                   CASE app_user.granted_tier WHEN 'plus' THEN 1 WHEN 'pro' THEN 2 WHEN 'unlimited' THEN 3 ELSE 0 END,
                   COALESCE((
                       SELECT MAX(CASE subscription.tier WHEN 'plus' THEN 1 WHEN 'pro' THEN 2 WHEN 'unlimited' THEN 3 ELSE 0 END)
                       FROM billing_subscription subscription
                       WHERE subscription.user_id = app_user.id
                         AND subscription.status IN ('active', 'trialing', 'past_due')), 0))] AS tier
        FROM app_user
        """;

    public async Task<StorageRows> GetStorageAsync(CancellationToken cancellationToken)
    {
        string sql = $"""
            SELECT COUNT(*) AS clips,
                   COALESCE(SUM({ClipsStatements.StorageBytesExpression}), 0)::bigint AS bytes,
                   COUNT(*) FILTER (WHERE binned_at IS NOT NULL) AS binned_clips,
                   COALESCE(SUM({ClipsStatements.StorageBytesExpression}) FILTER (WHERE binned_at IS NOT NULL), 0)::bigint
                       AS binned_bytes
            FROM clip;

            WITH usage AS (
                SELECT owner_id, COUNT(*) AS clips, SUM({ClipsStatements.StorageBytesExpression})::bigint AS bytes
                FROM clip
                GROUP BY owner_id
            ),
            account_tier AS ({AccountTierSql})
            SELECT account_tier.tier, COUNT(*) AS accounts, SUM(usage.clips)::bigint AS clips,
                   SUM(usage.bytes)::bigint AS bytes
            FROM usage
            JOIN account_tier ON account_tier.id = usage.owner_id
            GROUP BY account_tier.tier;

            WITH usage AS (
                SELECT owner_id, COUNT(*) AS clips, SUM({ClipsStatements.StorageBytesExpression})::bigint AS bytes
                FROM clip
                GROUP BY owner_id
                ORDER BY bytes DESC, owner_id
                LIMIT @TopAccountLimit
            ),
            account_tier AS ({AccountTierSql} WHERE app_user.id IN (SELECT owner_id FROM usage))
            SELECT usage.owner_id AS user_id, account_tier.username, account_tier.tier, usage.clips, usage.bytes
            FROM usage
            JOIN account_tier ON account_tier.id = usage.owner_id
            ORDER BY usage.bytes DESC, usage.owner_id;

            SELECT COUNT(*) FILTER (WHERE status = 'ready') AS held,
                   COALESCE(SUM((
                       SELECT COALESCE(SUM((part ->> 'size_bytes')::bigint), 0)
                       FROM jsonb_array_elements(parts) part)) FILTER (WHERE status = 'ready'), 0)::bigint AS held_bytes,
                   COUNT(*) FILTER (WHERE status = 'pending') AS pending,
                   COUNT(*) FILTER (WHERE status = 'running') AS running
            FROM clip_export;
            """;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(
            new CommandDefinition(sql, new { TopAccountLimit }, cancellationToken: cancellationToken));
        TotalRow total = await grid.ReadSingleAsync<TotalRow>();
        List<TierRow> tiers = (await grid.ReadAsync<TierRow>()).ToList();
        List<AccountRow> accounts = (await grid.ReadAsync<AccountRow>()).ToList();
        ExportRow exports = await grid.ReadSingleAsync<ExportRow>();
        return new StorageRows(total, tiers, accounts, exports);
    }

    /// <summary>The latest failed transcode jobs, newest first.</summary>
    public async Task<List<TranscodeFailureRow>> GetTranscodeFailuresAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id AS run_id, clip_id, type, error, attempts, finished_at
            FROM transcode_run
            WHERE status = 'failed'
            ORDER BY finished_at DESC NULLS LAST, id
            LIMIT @FailureLimit
            """;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return (await connection.QueryAsync<TranscodeFailureRow>(
            new CommandDefinition(sql, new { FailureLimit }, cancellationToken: cancellationToken))).ToList();
    }

    /// <summary>
    /// Ready R2 clips whose output still carries Opus audio: what the AAC backfill has left, by the clips it picks
    /// (<c>TranscodeStatements.EnqueueAacBackfillAsync</c>), queued or not.
    /// </summary>
    public async Task<long> CountAacBackfillRemainingAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COUNT(*) FROM clip
            WHERE video_host = 'r2' AND video_state = 'ready' AND video_codecs LIKE '%,opus'
            """;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));
    }

    /// <summary>Each AI queue's latest failures, newest first, keyed by queue.</summary>
    public async Task<Dictionary<string, List<AiFailureRow>>> GetAiFailuresAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id AS run_id, clip_id, trigger, provider, model, error, attempts, completed_at
            FROM legend_detection_run
            WHERE status = 'failed' AND archived_at IS NULL
            ORDER BY completed_at DESC NULLS LAST, id
            LIMIT @FailureLimit;

            SELECT id AS run_id, clip_id, trigger, NULL::text AS provider, model, error, attempts, completed_at
            FROM clip_transcription_run
            WHERE status = 'failed'
            ORDER BY completed_at DESC NULLS LAST, id
            LIMIT @FailureLimit;

            SELECT id AS run_id, clip_id, trigger, provider, model, error, attempts, completed_at
            FROM clip_summary_run
            WHERE status = 'failed'
            ORDER BY completed_at DESC NULLS LAST, id
            LIMIT @FailureLimit;
            """;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(
            new CommandDefinition(sql, new { FailureLimit }, cancellationToken: cancellationToken));
        return new Dictionary<string, List<AiFailureRow>>
        {
            [OpsService.LegendDetectionQueue] = (await grid.ReadAsync<AiFailureRow>()).ToList(),
            [OpsService.ClipTranscriptionQueue] = (await grid.ReadAsync<AiFailureRow>()).ToList(),
            [OpsService.ClipSummaryQueue] = (await grid.ReadAsync<AiFailureRow>()).ToList()
        };
    }

    /// <summary>
    /// Runs finished (succeeded or failed, archived and eval runs included, since they were paid for) in the last
    /// <paramref name="month"/>, and of those in the last <paramref name="day"/>, by queue, provider and model. Only
    /// legend detection has a cost.
    /// </summary>
    public async Task<List<AiSpendRow>> GetAiSpendAsync(TimeSpan day, TimeSpan month, CancellationToken cancellationToken)
    {
        string sql = $"""
            WITH runs AS (
                SELECT '{OpsService.LegendDetectionQueue}' AS queue, run.provider, run.model, run.input_tokens,
                       run.output_tokens, {LegendDetectionStatements.RunCostSql} AS cost, run.completed_at
                FROM legend_detection_run run
                LEFT JOIN legend_model_price price ON price.provider = run.provider AND price.model = run.model
                WHERE run.status IN ('succeeded', 'failed') AND run.completed_at >= now() - @Month
                UNION ALL
                SELECT '{OpsService.ClipTranscriptionQueue}', NULL, run.model, run.input_tokens, run.output_tokens,
                       NULL::numeric, run.completed_at
                FROM clip_transcription_run run
                WHERE run.status IN ('succeeded', 'failed') AND run.completed_at >= now() - @Month
                UNION ALL
                SELECT '{OpsService.ClipSummaryQueue}', run.provider, run.model, run.input_tokens, run.output_tokens,
                       NULL::numeric, run.completed_at
                FROM clip_summary_run run
                WHERE run.status IN ('succeeded', 'failed') AND run.completed_at >= now() - @Month
            )
            SELECT queue, provider, model,
                   COUNT(*) FILTER (WHERE completed_at >= now() - @Day) AS day_runs,
                   COALESCE(SUM(input_tokens) FILTER (WHERE completed_at >= now() - @Day), 0)::bigint AS day_input_tokens,
                   COALESCE(SUM(output_tokens) FILTER (WHERE completed_at >= now() - @Day), 0)::bigint AS day_output_tokens,
                   SUM(cost) FILTER (WHERE completed_at >= now() - @Day) AS day_cost,
                   COUNT(*) FILTER (WHERE completed_at >= now() - @Day AND cost IS NULL) AS day_unpriced,
                   COUNT(*) AS month_runs,
                   COALESCE(SUM(input_tokens), 0)::bigint AS month_input_tokens,
                   COALESCE(SUM(output_tokens), 0)::bigint AS month_output_tokens,
                   SUM(cost) AS month_cost,
                   COUNT(*) FILTER (WHERE cost IS NULL) AS month_unpriced
            FROM runs
            GROUP BY queue, provider, model
            ORDER BY queue, SUM(cost) DESC NULLS LAST, COUNT(*) DESC, provider, model
            """;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return (await connection.QueryAsync<AiSpendRow>(new CommandDefinition(sql, new { Day = day, Month = month },
            cancellationToken: cancellationToken))).ToList();
    }

    /// <summary>How long a day's sample of the database's storage total is kept.</summary>
    public static readonly TimeSpan StorageSampleRetention = TimeSpan.FromDays(90);

    /// <summary>
    /// Records <paramref name="databaseBytes"/> as <paramref name="now"/>'s UTC day's total, at most every
    /// <paramref name="every"/> (so a day ends with a reading from near its end), drops days older than
    /// <see cref="StorageSampleRetention"/>, and answers the days from <paramref name="from"/> on, oldest first. Two
    /// instances recording at once both land, the later reading winning.
    /// </summary>
    public async Task<List<StorageSampleRow>> RecordStorageSampleAsync(long databaseBytes, DateTimeOffset now,
        TimeSpan every, DateOnly from, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO ops_storage_sample (day, database_bytes, recorded_at)
            VALUES (@Day::date, @DatabaseBytes, @Now)
            ON CONFLICT (day) DO UPDATE
                SET database_bytes = EXCLUDED.database_bytes, recorded_at = EXCLUDED.recorded_at
                WHERE ops_storage_sample.recorded_at <= EXCLUDED.recorded_at - @Every;

            DELETE FROM ops_storage_sample WHERE day < @Oldest::date;

            SELECT to_char(day, 'YYYY-MM-DD') AS day, database_bytes
            FROM ops_storage_sample
            WHERE day >= @From::date
            ORDER BY day;
            """;
        DateOnly today = DateOnly.FromDateTime(now.UtcDateTime);
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(new CommandDefinition(sql, new
        {
            Day = Iso(today),
            DatabaseBytes = databaseBytes,
            Now = now,
            Every = every,
            Oldest = Iso(today.AddDays(-(int)StorageSampleRetention.TotalDays)),
            From = Iso(from)
        }, cancellationToken: cancellationToken));
        return (await grid.ReadAsync<StorageSampleRow>()).ToList();
    }

    private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A day's database total; <see cref="Day"/> is <c>yyyy-MM-dd</c>.</summary>
    public sealed class StorageSampleRow
    {
        public string Day { get; set; } = "";
        public long DatabaseBytes { get; set; }
    }

    public sealed record StorageRows(TotalRow Total, List<TierRow> Tiers, List<AccountRow> Accounts, ExportRow Exports);

    public sealed class TotalRow
    {
        public long Clips { get; set; }
        public long Bytes { get; set; }
        public long BinnedClips { get; set; }
        public long BinnedBytes { get; set; }
    }

    public sealed class TierRow
    {
        public string Tier { get; set; } = "";
        public long Accounts { get; set; }
        public long Clips { get; set; }
        public long Bytes { get; set; }
    }

    public sealed class AccountRow
    {
        public Guid UserId { get; set; }
        public string Username { get; set; } = "";
        public string Tier { get; set; } = "";
        public long Clips { get; set; }
        public long Bytes { get; set; }
    }

    public sealed class ExportRow
    {
        public long Held { get; set; }
        public long HeldBytes { get; set; }
        public long Pending { get; set; }
        public long Running { get; set; }
    }

    public sealed class TranscodeFailureRow
    {
        public Guid RunId { get; set; }
        public Guid ClipId { get; set; }
        public string Type { get; set; } = "";
        public string? Error { get; set; }
        public int Attempts { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }
    }

    public sealed class AiFailureRow
    {
        public Guid RunId { get; set; }
        public Guid ClipId { get; set; }
        public string Trigger { get; set; } = "";
        public string? Provider { get; set; }
        public string Model { get; set; } = "";
        public string? Error { get; set; }
        public int Attempts { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
    }

    public sealed class AiSpendRow
    {
        public string Queue { get; set; } = "";
        public string? Provider { get; set; }
        public string Model { get; set; } = "";
        public long DayRuns { get; set; }
        public long DayInputTokens { get; set; }
        public long DayOutputTokens { get; set; }
        public decimal? DayCost { get; set; }
        public long DayUnpriced { get; set; }
        public long MonthRuns { get; set; }
        public long MonthInputTokens { get; set; }
        public long MonthOutputTokens { get; set; }
        public decimal? MonthCost { get; set; }
        public long MonthUnpriced { get; set; }
    }
}
