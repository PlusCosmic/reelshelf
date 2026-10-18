using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using Reelshelf.Video.Transcoding;

namespace Reelshelf.QueueMetrics;

/// <summary>
/// Samples <c>transcode_run</c>, the video pipeline's job queue, as the <c>transcode</c> queue with its job
/// <c>type</c> as the trigger. It is shaped differently from the other run tables: the retry delay is
/// <c>available_at</c>, staleness is <c>heartbeat_at</c> against <see cref="TranscodingOptions.StaleAfter"/>, and
/// jobs end at <c>finished_at</c>. The overflow scaler counts its own backlog (<c>OverflowBacklogStatements</c>).
/// </summary>
/// <remarks>
/// A job that has used up <see cref="TranscodingOptions.MaxAttempts"/> is only failed by the next claim
/// (<c>FailExhaustedRunsAsync</c>), so until then it sits pending, or running with a quiet worker. Such jobs count
/// as exhausted straight away. The pending ones are left out of pending, retrying and the oldest age, since no
/// worker will ever take them; the running ones still count as running and stale.
/// </remarks>
public sealed class TranscodeRunMetricsSource(NpgsqlDataSource dataSource, IOptionsMonitor<TranscodingOptions> options)
    : IRunQueueMetricsSource
{
    public const string QueueName = "transcode";

    /// <summary>
    /// Three grouped counts, each with a status condition matching a partial index's predicate exactly, so Postgres
    /// can use it: pending rows (<c>transcode_run_pending_idx</c>), running rows (<c>transcode_run_running_idx</c>)
    /// and rows finished in the last hour (<c>transcode_run_finished_idx</c>, V43). Pending is split the way the
    /// claim splits it, by <c>available_at</c>, and exhausted the way <c>FailExhaustedRunsAsync</c> picks jobs.
    /// </summary>
    internal const string Sql = """
        SELECT type AS trigger,
               COUNT(*) FILTER (WHERE attempts < @MaxAttempts AND available_at <= now()) AS pending,
               COUNT(*) FILTER (WHERE attempts < @MaxAttempts AND available_at > now()) AS retrying,
               COALESCE(EXTRACT(EPOCH FROM now() - MIN(created_at) FILTER (WHERE attempts < @MaxAttempts)), 0)::float8
                   AS oldest_pending_age_seconds,
               COUNT(*) FILTER (WHERE attempts >= @MaxAttempts) AS exhausted
        FROM transcode_run
        WHERE status = 'pending'
        GROUP BY type;

        SELECT type AS trigger,
               COUNT(*) AS running,
               COUNT(*) FILTER (WHERE heartbeat_at < now() - @StaleAfter) AS stale_running,
               COUNT(*) FILTER (WHERE heartbeat_at < now() - @StaleAfter AND attempts >= @MaxAttempts) AS exhausted
        FROM transcode_run
        WHERE status = 'running'
        GROUP BY type;

        SELECT type AS trigger,
               COUNT(*) FILTER (WHERE status = 'succeeded') AS succeeded,
               COUNT(*) FILTER (WHERE status = 'failed') AS failed,
               COUNT(*) FILTER (WHERE status = 'failed' AND attempts >= @MaxAttempts) AS exhausted
        FROM transcode_run
        WHERE status IN ('succeeded', 'failed') AND finished_at >= now() - @Window
        GROUP BY type;
        """;

    public string Queue => QueueName;

    public async Task<IReadOnlyList<RunQueueSample>> SampleAsync(CancellationToken cancellationToken)
    {
        TranscodingOptions current = options.CurrentValue;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        CommandDefinition command = new(Sql, new
        {
            MaxAttempts = current.EffectiveMaxAttempts,
            current.StaleAfter,
            Window = RunTableMetricsSource.RecentWindow
        }, cancellationToken: cancellationToken);
        await using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(command);
        List<PendingRow> pending = (await grid.ReadAsync<PendingRow>()).ToList();
        List<RunningRow> running = (await grid.ReadAsync<RunningRow>()).ToList();
        List<FinishedRow> finished = (await grid.ReadAsync<FinishedRow>()).ToList();

        Dictionary<string, RunQueueSample> samples = TranscodeRunTypes.All.ToDictionary(type => type, RunQueueSample.Empty);
        RunQueueSample For(string trigger) => samples.GetValueOrDefault(trigger, RunQueueSample.Empty(trigger));
        foreach (PendingRow row in pending)
        {
            RunQueueSample sample = For(row.Trigger);
            samples[row.Trigger] = sample with
            {
                Pending = row.Pending,
                Retrying = row.Retrying,
                OldestPendingAgeSeconds = row.OldestPendingAgeSeconds,
                ExhaustedLastHour = sample.ExhaustedLastHour + row.Exhausted
            };
        }

        foreach (RunningRow row in running)
        {
            RunQueueSample sample = For(row.Trigger);
            samples[row.Trigger] = sample with
            {
                Running = row.Running,
                StaleRunning = row.StaleRunning,
                ExhaustedLastHour = sample.ExhaustedLastHour + row.Exhausted
            };
        }

        foreach (FinishedRow row in finished)
        {
            RunQueueSample sample = For(row.Trigger);
            samples[row.Trigger] = sample with
            {
                SucceededLastHour = row.Succeeded,
                FailedLastHour = row.Failed,
                ExhaustedLastHour = sample.ExhaustedLastHour + row.Exhausted
            };
        }

        return samples.Values.OrderBy(sample => sample.Trigger, StringComparer.Ordinal).ToList();
    }

    internal sealed class PendingRow
    {
        public string Trigger { get; set; } = "";
        public long Pending { get; set; }
        public long Retrying { get; set; }
        public double OldestPendingAgeSeconds { get; set; }
        public long Exhausted { get; set; }
    }

    internal sealed class RunningRow
    {
        public string Trigger { get; set; } = "";
        public long Running { get; set; }
        public long StaleRunning { get; set; }
        public long Exhausted { get; set; }
    }

    internal sealed class FinishedRow
    {
        public string Trigger { get; set; } = "";
        public long Succeeded { get; set; }
        public long Failed { get; set; }
        public long Exhausted { get; set; }
    }
}
