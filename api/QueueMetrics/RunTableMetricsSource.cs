using System.Text.RegularExpressions;
using Dapper;
using Npgsql;

namespace Reelshelf.QueueMetrics;

/// <summary>
/// A queue kept in a Postgres run table with the usual columns: <c>trigger</c>, <c>status</c> (pending, running,
/// succeeded, failed), <c>attempts</c>, <c>created_at</c>, <c>started_at</c> and <c>completed_at</c>, claimed the way
/// <c>ClaimNextRunAsync</c> claims them.
/// </summary>
/// <param name="Queue">The <c>queue</c> tag.</param>
/// <param name="Table">The run table.</param>
/// <param name="RetryAfter">How long a failed attempt waits before it is claimed again, as the claim query uses it.</param>
/// <param name="StaleAfter">How long a run may stay running before it is claimed again, as the claim query uses it.</param>
/// <param name="Triggers">Triggers reported even before the table has a row for them, so their gauges start at zero.</param>
/// <param name="Filter">An extra SQL condition on the table's rows, e.g. <c>archived_at IS NULL</c>.</param>
public sealed record RunTableQueue(
    string Queue,
    string Table,
    TimeSpan RetryAfter,
    TimeSpan StaleAfter,
    IReadOnlyList<string> Triggers,
    string? Filter = null);

/// <summary>
/// Samples a <see cref="RunTableQueue"/> with two grouped counts: one over pending and running rows (the partial
/// <c>*_queue_idx</c> index) and one over rows finished in the last hour (the partial <c>*_finished_idx</c> index).
/// </summary>
public sealed partial class RunTableMetricsSource : IRunQueueMetricsSource
{
    /// <summary>How far back the succeeded, failed and exhausted counts look.</summary>
    public static readonly TimeSpan RecentWindow = TimeSpan.FromHours(1);

    private readonly RunTableQueue queue;
    private readonly NpgsqlDataSource dataSource;
    private readonly Func<int> maxAttempts;
    private readonly string sql;

    /// <param name="maxAttempts">
    /// The queue's current attempt limit: a failed run with at least this many attempts is exhausted.
    /// </param>
    public RunTableMetricsSource(RunTableQueue queue, NpgsqlDataSource dataSource, Func<int> maxAttempts)
    {
        if (!IdentifierPattern().IsMatch(queue.Table))
        {
            throw new ArgumentException($"'{queue.Table}' is not a table name", nameof(queue));
        }

        this.queue = queue;
        this.dataSource = dataSource;
        this.maxAttempts = maxAttempts;
        sql = BuildSql(queue);
    }

    public string Queue => queue.Queue;

    public async Task<IReadOnlyList<RunQueueSample>> SampleAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        CommandDefinition command = new(sql, new
        {
            queue.RetryAfter,
            queue.StaleAfter,
            Window = RecentWindow,
            MaxAttempts = maxAttempts()
        }, cancellationToken: cancellationToken);
        await using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(command);
        List<OpenRow> open = (await grid.ReadAsync<OpenRow>()).ToList();
        List<FinishedRow> finished = (await grid.ReadAsync<FinishedRow>()).ToList();

        Dictionary<string, RunQueueSample> samples = queue.Triggers.ToDictionary(trigger => trigger, RunQueueSample.Empty);
        foreach (OpenRow row in open)
        {
            samples[row.Trigger] = samples.GetValueOrDefault(row.Trigger, RunQueueSample.Empty(row.Trigger)) with
            {
                Pending = row.Pending,
                Retrying = row.Retrying,
                OldestPendingAgeSeconds = row.OldestPendingAgeSeconds,
                Running = row.Running,
                StaleRunning = row.StaleRunning
            };
        }

        foreach (FinishedRow row in finished)
        {
            samples[row.Trigger] = samples.GetValueOrDefault(row.Trigger, RunQueueSample.Empty(row.Trigger)) with
            {
                SucceededLastHour = row.Succeeded,
                FailedLastHour = row.Failed,
                ExhaustedLastHour = row.Exhausted
            };
        }

        return samples.Values.OrderBy(sample => sample.Trigger, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// The two queries. Their status lists match the partial indexes' predicates exactly, so Postgres can use
    /// them; pending is split the way the claim query splits it.
    /// </summary>
    internal static string BuildSql(RunTableQueue queue)
    {
        string filter = string.IsNullOrWhiteSpace(queue.Filter) ? "" : $" AND ({queue.Filter})";
        return $"""
            SELECT trigger,
                   COUNT(*) FILTER (WHERE status = 'pending'
                       AND (started_at IS NULL OR started_at < now() - @RetryAfter)) AS pending,
                   COUNT(*) FILTER (WHERE status = 'pending' AND started_at >= now() - @RetryAfter) AS retrying,
                   COALESCE(EXTRACT(EPOCH FROM now() - MIN(created_at) FILTER (WHERE status = 'pending')), 0)::float8
                       AS oldest_pending_age_seconds,
                   COUNT(*) FILTER (WHERE status = 'running') AS running,
                   COUNT(*) FILTER (WHERE status = 'running' AND started_at < now() - @StaleAfter) AS stale_running
            FROM {queue.Table}
            WHERE status IN ('pending', 'running'){filter}
            GROUP BY trigger;

            SELECT trigger,
                   COUNT(*) FILTER (WHERE status = 'succeeded') AS succeeded,
                   COUNT(*) FILTER (WHERE status = 'failed') AS failed,
                   COUNT(*) FILTER (WHERE status = 'failed' AND attempts >= @MaxAttempts) AS exhausted
            FROM {queue.Table}
            WHERE status IN ('succeeded', 'failed') AND completed_at >= now() - @Window{filter}
            GROUP BY trigger;
            """;
    }

    [GeneratedRegex("^[a-z_][a-z0-9_]*$")]
    private static partial Regex IdentifierPattern();

    internal sealed class OpenRow
    {
        public string Trigger { get; set; } = "";
        public long Pending { get; set; }
        public long Retrying { get; set; }
        public double OldestPendingAgeSeconds { get; set; }
        public long Running { get; set; }
        public long StaleRunning { get; set; }
    }

    internal sealed class FinishedRow
    {
        public string Trigger { get; set; } = "";
        public long Succeeded { get; set; }
        public long Failed { get; set; }
        public long Exhausted { get; set; }
    }
}
