using Dapper;
using Npgsql;
using Reelshelf.Core;
using Reelshelf.Video.Transcoding;
using Reelshelf.Video.Transcoding.Overflow;
using Xunit;

namespace Reelshelf.Test.Video;

/// <summary>
/// Runs the overflow pool's backlog count (<see cref="OverflowBacklogStatements"/>) against a real Postgres, on
/// <c>transcode_run</c> as V40 and V44 make it, in a schema of the test's own. Only jobs the claim would hand out
/// count: pending, past <c>available_at</c>, with attempts left.
/// </summary>
public class OverflowBacklogTests
{
    private const int MaxAttempts = 3;

    [PostgresFact]
    public async Task Backlog_CountsOnlyClaimableJobs_AndAgesTheOldestFromWhenItBecameClaimable()
    {
        await using TestSchema test = await TestSchema.CreateAsync();
        await SeedAsync(test.Connection);
        OverflowBacklogStatements statements = new(test.DataSource);

        OverflowBacklog all = await statements.ReadAsync(MaxAttempts, [], CancellationToken.None);
        Assert.Equal(4, all.Pending);
        // The released migrate job was queued 3 hours ago but has only been claimable for 30 minutes; the trim job,
        // claimable for 45, is the oldest.
        Assert.InRange(all.OldestPendingAge.TotalMinutes, 44, 46);

        OverflowBacklog transcodes = await statements.ReadAsync(MaxAttempts, [TranscodeRunTypes.Transcode],
            CancellationToken.None);
        Assert.Equal(2, transcodes.Pending);
        Assert.InRange(transcodes.OldestPendingAge.TotalMinutes, 9, 11);

        OverflowBacklog migrations = await statements.ReadAsync(MaxAttempts,
            [TranscodeRunTypes.Migrate, TranscodeRunTypes.Thumbnails], CancellationToken.None);
        Assert.Equal(1, migrations.Pending);
        Assert.InRange(migrations.OldestPendingAge.TotalMinutes, 29, 31);

        // A higher attempt limit makes the job out of attempts claimable again, as it would the claim.
        Assert.Equal(5, (await statements.ReadAsync(MaxAttempts + 1, [], CancellationToken.None)).Pending);
    }

    [PostgresFact]
    public async Task Backlog_IsEmptyWithNothingClaimable()
    {
        await using TestSchema test = await TestSchema.CreateAsync();
        await AddRunAsync(test.Connection, "transcode", "pending", 0, created: 60, available: -5);

        Assert.Equal(OverflowBacklog.Empty, await new OverflowBacklogStatements(test.DataSource)
            .ReadAsync(MaxAttempts, [], CancellationToken.None));
    }

    [PostgresFact]
    public async Task Backlog_CountsWhatTheClaimTakes()
    {
        await using TestSchema test = await TestSchema.CreateAsync();
        await SeedAsync(test.Connection);
        OverflowBacklogStatements backlog = new(test.DataSource);
        TranscodeStatements claims = new(test.DataSource.CreateConnection());
        // Stale running jobs are claimable too but aren't backlog; take them out of the way first.
        await test.Connection.ExecuteAsync("DELETE FROM transcode_run WHERE status = 'running'");

        long pending = (await backlog.ReadAsync(MaxAttempts, [], CancellationToken.None)).Pending;
        int claimed = 0;
        while (await claims.ClaimNextRunAsync(MaxAttempts, TimeSpan.FromHours(1)) is not null)
        {
            claimed++;
        }

        Assert.Equal(pending, claimed);
        Assert.Equal(0, (await backlog.ReadAsync(MaxAttempts, [], CancellationToken.None)).Pending);
    }

    [PostgresFact]
    public async Task Backlog_CanUseThePendingIndex()
    {
        await using TestSchema test = await TestSchema.CreateAsync();
        await SeedAsync(test.Connection);
        await test.Connection.ExecuteAsync("ANALYZE transcode_run");
        // A table this small is cheapest to scan whole, so keep the planner off that to see which index it can use.
        await test.Connection.ExecuteAsync("SET enable_seqscan = off");

        string plan = string.Join('\n', await test.Connection.QueryAsync<string>(
            $"EXPLAIN {OverflowBacklogStatements.Sql}",
            new { MaxAttempts, JobTypes = new[] { TranscodeRunTypes.Transcode } }));

        Assert.Contains("transcode_run_pending_idx", plan);
    }

    private static async Task SeedAsync(NpgsqlConnection connection)
    {
        // (type, status, attempts, created, available) as minutes ago; a negative available is still to come.
        await AddRunAsync(connection, "transcode", "pending", 0, created: 10, available: 10); // claimable
        await AddRunAsync(connection, "transcode", "pending", 1, created: 40, available: 1); // retry delay over
        await AddRunAsync(connection, "transcode", "pending", 1, created: 20, available: -1); // waiting out the retry
        await AddRunAsync(connection, "transcode", "pending", MaxAttempts, created: 90, available: 80); // out of attempts
        await AddRunAsync(connection, "transcode", "running", 1, created: 300, available: 300, heartbeat: 120); // stale
        await AddRunAsync(connection, "transcode", "running", 1, created: 30, available: 30, heartbeat: 1);
        await AddRunAsync(connection, "transcode", "succeeded", 1, created: 500, available: 500);
        await AddRunAsync(connection, "transcode", "failed", MaxAttempts, created: 500, available: 500);
        await AddRunAsync(connection, "trim", "pending", 0, created: 45, available: 45); // claimable
        await AddRunAsync(connection, "migrate", "pending", 0, created: 180, available: 30); // released by a worker
    }

    private static async Task AddRunAsync(
        NpgsqlConnection connection,
        string type,
        string status,
        int attempts,
        int created,
        int available,
        int? heartbeat = null)
    {
        // A clip has at most one pending or running transcode, so each job gets a clip of its own.
        Guid clipId = Guid.NewGuid();
        await connection.ExecuteAsync("INSERT INTO clip (id) VALUES (@ClipId)", new { ClipId = clipId });
        await connection.ExecuteAsync("""
            INSERT INTO transcode_run (clip_id, type, status, attempts, created_at, available_at, heartbeat_at,
                                       lease_id)
            VALUES (@ClipId, @Type, @Status, @Attempts, now() - make_interval(mins => @Created),
                    now() - make_interval(mins => @Available), now() - make_interval(mins => @Heartbeat),
                    CASE WHEN @Status = 'running' THEN gen_random_uuid() END)
            """, new
        {
            ClipId = clipId, Type = type, Status = status, Attempts = attempts, Created = created,
            Available = available, Heartbeat = heartbeat
        });
    }

    private sealed class TestSchema(NpgsqlDataSource dataSource, NpgsqlConnection connection, string schema)
        : IAsyncDisposable
    {
        public NpgsqlDataSource DataSource => dataSource;
        public NpgsqlConnection Connection => connection;

        public static async Task<TestSchema> CreateAsync()
        {
            DefaultTypeMap.MatchNamesWithUnderscores = true;
            string schema = $"overflow_test_{Guid.NewGuid():N}";
            await using (NpgsqlConnection admin = new(PostgresFactAttribute.ConnectionString))
            {
                await admin.OpenAsync();
                await admin.ExecuteAsync($"CREATE SCHEMA {schema}");
            }

            NpgsqlDataSource dataSource = ReelshelfDataSource.Create(
                $"{PostgresFactAttribute.ConnectionString};Search Path={schema}");
            NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
            // The claim joins clip for its current video; only these columns matter here.
            await connection.ExecuteAsync("""
                CREATE TABLE clip (
                    id uuid PRIMARY KEY,
                    video_version integer NOT NULL DEFAULT 1,
                    video_host text NOT NULL DEFAULT 'r2',
                    video_id uuid NOT NULL DEFAULT gen_random_uuid())
                """);
            await connection.ExecuteAsync(await File.ReadAllTextAsync(MigrationPath("V40__transcode_run.sql")));
            await connection.ExecuteAsync(await File.ReadAllTextAsync(MigrationPath("V44__bunny_migration.sql")));
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
}
