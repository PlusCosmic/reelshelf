using Dapper;
using Npgsql;
using Reelshelf.Core;
using Reelshelf.QueueMetrics;
using Xunit;

namespace Reelshelf.Test.QueueMetrics;

/// <summary>
/// Runs each run table's sampling queries against a real Postgres, with a row in every state the metrics tell
/// apart. Tables with the columns the queries touch are made in a schema of the test's own, which is dropped
/// afterwards.
/// </summary>
public class RunQueueSamplingTests
{
    private const int MaxAttempts = 3;

    [PostgresFact]
    public async Task LegendDetection_IsSampledByTrigger_LeavingOutArchivedRuns() =>
        await SampleAsync(QueueMetricsSetup.LegendDetectionQueue);

    [PostgresFact]
    public async Task ClipTranscription_IsSampledByTrigger() =>
        await SampleAsync(QueueMetricsSetup.ClipTranscriptionQueue);

    [PostgresFact]
    public async Task ClipSummary_IsSampledByTrigger() => await SampleAsync(QueueMetricsSetup.ClipSummaryQueue);

    private static async Task SampleAsync(RunTableQueue queue)
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        string schema = $"queue_metrics_test_{Guid.NewGuid():N}";
        await using (NpgsqlConnection admin = new(PostgresFactAttribute.ConnectionString))
        {
            await admin.OpenAsync();
            await admin.ExecuteAsync($"CREATE SCHEMA {schema}");
        }

        await using NpgsqlDataSource dataSource = ReelshelfDataSource.Create(
            $"{PostgresFactAttribute.ConnectionString};Search Path={schema}");
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
        try
        {
            await SeedAsync(connection, queue);
            RunTableMetricsSource source = new(queue, dataSource, () => MaxAttempts);
            Dictionary<string, RunQueueSample> samples =
                (await source.SampleAsync(CancellationToken.None)).ToDictionary(sample => sample.Trigger);

            // Every known trigger is reported, quiet ones as zeros.
            Assert.Equal(queue.Triggers.Order(), samples.Keys.Order());

            RunQueueSample auto = samples["auto"];
            Assert.Equal(2, auto.Pending);
            Assert.Equal(1, auto.Retrying);
            Assert.InRange(auto.OldestPendingAgeSeconds, 40 * 60 - 60, 40 * 60 + 60);
            Assert.Equal(2, auto.Running);
            Assert.Equal(1, auto.StaleRunning);
            Assert.Equal(1, auto.SucceededLastHour);
            Assert.Equal(2, auto.FailedLastHour);
            Assert.Equal(1, auto.ExhaustedLastHour);

            RunQueueSample manual = samples["manual"];
            Assert.Equal(1, manual.Pending);
            Assert.InRange(manual.OldestPendingAgeSeconds, 4 * 60, 6 * 60);
            Assert.Equal(0, manual.Running);

            foreach (RunQueueSample quiet in samples.Values.Where(s => s.Trigger is not ("auto" or "manual")))
            {
                Assert.Equal(RunQueueSample.Empty(quiet.Trigger), quiet);
            }
        }
        finally
        {
            await connection.ExecuteAsync($"DROP SCHEMA {schema} CASCADE");
        }
    }

    private static async Task SeedAsync(NpgsqlConnection connection, RunTableQueue queue)
    {
        bool archivable = queue.Filter?.Contains("archived_at") == true;
        await connection.ExecuteAsync($"""
            CREATE TABLE {queue.Table} (
                id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                trigger text NOT NULL,
                status text NOT NULL DEFAULT 'pending',
                attempts int NOT NULL DEFAULT 0,
                created_at timestamptz NOT NULL DEFAULT now(),
                started_at timestamptz,
                completed_at timestamptz
                {(archivable ? ", archived_at timestamptz" : "")}
            )
            """);

        // (trigger, status, attempts, created, started, completed) as minutes ago.
        (string, string, int, int, int?, int?)[] rows =
        [
            ("auto", "pending", 0, 10, null, null), // claimable: never started
            ("auto", "pending", 1, 40, 5, null), // claimable: retry delay over
            ("auto", "pending", 1, 20, 0, null), // waiting out the retry delay
            ("auto", "running", 1, 30, 1, null), // running
            ("auto", "running", 1, 60, 30, null), // stale: past every queue's stale-after
            ("auto", "succeeded", 1, 30, 20, 19), // succeeded in the last hour
            ("auto", "succeeded", 1, 200, 190, 189), // succeeded too long ago
            ("auto", "failed", MaxAttempts, 30, 20, 19), // used up its attempts
            ("auto", "failed", 1, 30, 20, 19), // failed for good on its first attempt
            ("auto", "failed", MaxAttempts, 300, 200, 180), // failed too long ago
            ("manual", "pending", 0, 5, null, null)
        ];
        foreach ((string trigger, string status, int attempts, int created, int? started, int? completed) in rows)
        {
            await connection.ExecuteAsync($"""
                INSERT INTO {queue.Table} (trigger, status, attempts, created_at, started_at, completed_at)
                VALUES (@Trigger, @Status, @Attempts, now() - make_interval(mins => @Created),
                        now() - make_interval(mins => @Started), now() - make_interval(mins => @Completed))
                """, new { Trigger = trigger, Status = status, Attempts = attempts, Created = created, Started = started, Completed = completed });
        }

        if (archivable)
        {
            // Archived runs are ignored by the worker, so none of these count.
            await connection.ExecuteAsync($"""
                INSERT INTO {queue.Table} (trigger, status, attempts, created_at, started_at, completed_at, archived_at)
                VALUES ('auto', 'pending', 0, now() - interval '3 hours', NULL, NULL, now()),
                       ('auto', 'running', 1, now() - interval '3 hours', now() - interval '2 hours', NULL, now()),
                       ('auto', 'failed', 3, now() - interval '30 minutes', now() - interval '20 minutes',
                        now() - interval '19 minutes', now())
                """);
        }
    }
}
