using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using Reelshelf.Core;
using Reelshelf.QueueMetrics;
using Reelshelf.Video.Transcoding;
using Xunit;

namespace Reelshelf.Test.QueueMetrics;

/// <summary>
/// Runs <see cref="TranscodeRunMetricsSource"/> against a real Postgres, on <c>transcode_run</c> as V40 and V43 make
/// it, with a job in every state the metrics tell apart. Each test has a schema of its own, dropped afterwards.
/// </summary>
public class TranscodeRunSamplingTests
{
    private const int MaxAttempts = 3;

    [PostgresFact]
    public async Task Transcode_IsSampledByType()
    {
        await using TestSchema test = await TestSchema.CreateAsync();
        await SeedAsync(test.Connection);
        TranscodeRunMetricsSource source = new(test.DataSource,
            new FixedOptions<TranscodingOptions>(new TranscodingOptions { MaxAttempts = MaxAttempts }));

        Dictionary<string, RunQueueSample> samples =
            (await source.SampleAsync(CancellationToken.None)).ToDictionary(sample => sample.Trigger);

        Assert.Equal("transcode", source.Queue);
        // Every job type is reported, quiet ones as zeros.
        Assert.Equal(TranscodeRunTypes.All.Order(), samples.Keys.Order());

        RunQueueSample transcode = samples[TranscodeRunTypes.Transcode];
        Assert.Equal(2, transcode.Pending);
        Assert.Equal(1, transcode.Retrying);
        // The exhausted pending job, queued 90 minutes ago, will never be claimed, so it isn't the oldest.
        Assert.InRange(transcode.OldestPendingAgeSeconds, 40 * 60 - 60, 40 * 60 + 60);
        Assert.Equal(4, transcode.Running);
        Assert.Equal(2, transcode.StaleRunning);
        Assert.Equal(1, transcode.SucceededLastHour);
        Assert.Equal(2, transcode.FailedLastHour);
        // One failed in the last hour, plus the pending and the stale running job the next claim will fail.
        Assert.Equal(3, transcode.ExhaustedLastHour);

        RunQueueSample trim = samples[TranscodeRunTypes.Trim];
        Assert.Equal(1, trim.Pending);
        Assert.InRange(trim.OldestPendingAgeSeconds, 4 * 60, 6 * 60);
        Assert.Equal(0, trim.Running);

        foreach (RunQueueSample quiet in samples.Values.Where(s =>
                     s.Trigger is not (TranscodeRunTypes.Transcode or TranscodeRunTypes.Trim)))
        {
            Assert.Equal(RunQueueSample.Empty(quiet.Trigger), quiet);
        }
    }

    [PostgresFact]
    public async Task SamplingQueries_CanUseThePartialIndexes()
    {
        await using TestSchema test = await TestSchema.CreateAsync();
        await SeedAsync(test.Connection);
        await test.Connection.ExecuteAsync("ANALYZE transcode_run");
        // A table this small is cheapest to scan whole, so keep the planner off that to see which index it can use.
        await test.Connection.ExecuteAsync("SET enable_seqscan = off");
        string[] statements = TranscodeRunMetricsSource.Sql.Split(';', StringSplitOptions.TrimEntries)
            .Where(statement => statement.Length > 0)
            .ToArray();
        string[] indexes = ["transcode_run_pending_idx", "transcode_run_running_idx", "transcode_run_finished_idx"];
        Assert.Equal(indexes.Length, statements.Length);

        for (int i = 0; i < statements.Length; i++)
        {
            string plan = string.Join('\n', await test.Connection.QueryAsync<string>($"EXPLAIN {statements[i]}", new
            {
                MaxAttempts,
                StaleAfter = TimeSpan.FromHours(1),
                Window = RunTableMetricsSource.RecentWindow
            }));
            Assert.Contains(indexes[i], plan);
        }
    }

    private static async Task SeedAsync(NpgsqlConnection connection)
    {
        // (type, status, attempts, created, available, heartbeat, finished) as minutes ago; negative is to come.
        (string, string, int, int, int, int?, int?)[] rows =
        [
            ("transcode", "pending", 0, 10, 10, null, null), // claimable: never tried
            ("transcode", "pending", 1, 40, 1, 45, null), // claimable: retry delay over
            ("transcode", "pending", 1, 20, -1, 5, null), // waiting out the retry delay
            ("transcode", "pending", MaxAttempts, 90, 1, 80, null), // out of attempts: the next claim fails it
            ("transcode", "running", 1, 30, 30, 1, null), // running
            ("transcode", "running", 1, 120, 120, 90, null), // stale: no report within the stale-after
            ("transcode", "running", MaxAttempts, 150, 100, 90, null), // stale on its last attempt: the next claim fails it
            ("transcode", "running", MaxAttempts, 150, 10, 1, null), // running its last attempt
            ("transcode", "succeeded", 1, 30, 30, 19, 19), // succeeded in the last hour
            ("transcode", "succeeded", 1, 200, 200, 189, 189), // succeeded too long ago
            ("transcode", "failed", MaxAttempts, 30, 25, 20, 19), // used up its attempts
            ("transcode", "failed", 1, 30, 30, 20, 19), // failed for good on its first attempt
            ("transcode", "failed", MaxAttempts, 300, 250, 200, 180), // failed too long ago
            ("trim", "pending", 0, 5, 5, null, null)
        ];
        foreach ((string type, string status, int attempts, int created, int available, int? heartbeat, int? finished) in rows)
        {
            // A clip has at most one pending or running transcode, so each job gets a clip of its own.
            Guid clipId = Guid.NewGuid();
            await connection.ExecuteAsync("INSERT INTO clip (id) VALUES (@ClipId)", new { ClipId = clipId });
            await connection.ExecuteAsync("""
                INSERT INTO transcode_run (clip_id, type, status, attempts, created_at, available_at, heartbeat_at,
                                           finished_at)
                VALUES (@ClipId, @Type, @Status, @Attempts, now() - make_interval(mins => @Created),
                        now() - make_interval(mins => @Available), now() - make_interval(mins => @Heartbeat),
                        now() - make_interval(mins => @Finished))
                """, new
            {
                ClipId = clipId, Type = type, Status = status, Attempts = attempts, Created = created,
                Available = available, Heartbeat = heartbeat, Finished = finished
            });
        }
    }

    private sealed class TestSchema(NpgsqlDataSource dataSource, NpgsqlConnection connection, string schema)
        : IAsyncDisposable
    {
        public NpgsqlDataSource DataSource => dataSource;
        public NpgsqlConnection Connection => connection;

        public static async Task<TestSchema> CreateAsync()
        {
            DefaultTypeMap.MatchNamesWithUnderscores = true;
            string schema = $"transcode_metrics_test_{Guid.NewGuid():N}";
            await using (NpgsqlConnection admin = new(PostgresFactAttribute.ConnectionString))
            {
                await admin.OpenAsync();
                await admin.ExecuteAsync($"CREATE SCHEMA {schema}");
            }

            NpgsqlDataSource dataSource = ReelshelfDataSource.Create(
                $"{PostgresFactAttribute.ConnectionString};Search Path={schema}");
            NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
            // transcode_run references clip; only its id matters here.
            await connection.ExecuteAsync("CREATE TABLE clip (id uuid PRIMARY KEY)");
            await connection.ExecuteAsync(await File.ReadAllTextAsync(MigrationPath("V40__transcode_run.sql")));
            await connection.ExecuteAsync(
                await File.ReadAllTextAsync(MigrationPath("V43__transcode_run_finished_index.sql")));
            return new TestSchema(dataSource, connection, schema);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await connection.ExecuteAsync($"DROP SCHEMA {schema} CASCADE");
            }
            finally
            {
                await connection.DisposeAsync();
                await dataSource.DisposeAsync();
            }
        }

        private static string MigrationPath(string fileName)
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Reelshelf.sln")))
            {
                directory = directory.Parent;
            }

            Assert.NotNull(directory);
            return Path.Combine(directory.FullName, "migrations", "db", "migrations", fileName);
        }
    }

    private sealed class FixedOptions<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
