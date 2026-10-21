using Dapper;
using Npgsql;
using Reelshelf.Core;
using Reelshelf.Video.Migration;
using Xunit;

namespace Reelshelf.Test.Video;

/// <summary>
/// Runs the Bunny migration's backfill selection (<see cref="BunnyMigrationStatements.QueueBatchAsync"/>) against a
/// real Postgres, on the minimal <c>clip</c> table and the video pipeline's migrations in a schema of the test's own.
/// </summary>
public class BunnyMigrationBackfillTests
{
    [PostgresFact]
    public async Task QueueBatch_TakesReadyBunnyClipsOldestFirst_UpToTheBatch_AndSkipsTheRest()
    {
        await using TestBackfill test = await TestBackfill.CreateAsync();
        Guid oldest = await test.AddClipAsync(daysAgo: 30);
        Guid older = await test.AddClipAsync(daysAgo: 20);
        Guid newer = await test.AddClipAsync(daysAgo: 10);
        Guid newest = await test.AddClipAsync(daysAgo: 1);
        // Never queued: nothing playable to move, already on R2, busy with another job, or migrated before.
        await test.AddClipAsync(daysAgo: 40, videoState: "encode_failed");
        await test.AddClipAsync(daysAgo: 40, videoHost: "r2");
        Guid busy = await test.AddClipAsync(daysAgo: 40);
        await test.AddRunAsync(busy, "thumbnails", "running");
        Guid failed = await test.AddClipAsync(daysAgo: 40);
        await test.AddRunAsync(failed, "migrate", "failed");
        Guid switchedBack = await test.AddClipAsync(daysAgo: 40);
        await test.AddRunAsync(switchedBack, "migrate", "succeeded");
        // A finished job of another type doesn't hold a clip back.
        await test.AddRunAsync(oldest, "thumbnails", "succeeded");

        // A clip switched back to Bunny after an earlier move keeps the version it reached on R2.
        await test.Connection.ExecuteAsync("UPDATE clip SET video_version = 3 WHERE id = @Older", new { Older = older });

        Assert.Equal([oldest, older], await test.Statements.QueueBatchAsync(batchSize: 2));
        // Each job writes the clip's next version, never keys it played from before (they are cached as immutable).
        Assert.Equal((10, 2, "{}"), await test.Connection.QuerySingleAsync<(int, int, string)>(
            "SELECT priority, video_version, input::text FROM transcode_run WHERE clip_id = @Oldest AND type = 'migrate'",
            new { Oldest = oldest }));
        Assert.Equal(4, await test.Connection.ExecuteScalarAsync<int>(
            "SELECT video_version FROM transcode_run WHERE clip_id = @Older AND type = 'migrate'", new { Older = older }));

        // The batch is full until a job finishes.
        Assert.Empty(await test.Statements.QueueBatchAsync(batchSize: 2));
        await test.Connection.ExecuteAsync(
            "UPDATE transcode_run SET status = 'succeeded' WHERE clip_id = @Oldest AND type = 'migrate'",
            new { Oldest = oldest });
        Assert.Equal([newer], await test.Statements.QueueBatchAsync(batchSize: 2));

        // A bigger batch takes what is left; nothing is queued twice.
        Assert.Equal([newest], await test.Statements.QueueBatchAsync(batchSize: 10));
        Assert.Empty(await test.Statements.QueueBatchAsync(batchSize: 10));
        Assert.Equal(1, await test.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM transcode_run WHERE clip_id = @Older AND type = 'migrate'", new { Older = older }));
    }

    private sealed class TestBackfill : IAsyncDisposable
    {
        private readonly NpgsqlDataSource _dataSource;
        private readonly string _schema;

        private TestBackfill(NpgsqlDataSource dataSource, NpgsqlConnection connection, string schema)
        {
            _dataSource = dataSource;
            Connection = connection;
            _schema = schema;
            Statements = new BunnyMigrationStatements(dataSource.CreateConnection());
        }

        /// <summary>A separate connection for arranging and checking rows.</summary>
        public NpgsqlConnection Connection { get; }

        public BunnyMigrationStatements Statements { get; }

        public static async Task<TestBackfill> CreateAsync()
        {
            DefaultTypeMap.MatchNamesWithUnderscores = true;
            string schema = $"migration_test_{Guid.NewGuid():N}";
            await using (NpgsqlConnection admin = new(PostgresFactAttribute.ConnectionString))
            {
                await admin.OpenAsync();
                await admin.ExecuteAsync($"CREATE SCHEMA {schema}");
            }

            NpgsqlDataSource dataSource = ReelshelfDataSource.Create(
                $"{PostgresFactAttribute.ConnectionString};Search Path={schema}");
            NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
            await TestClipSchema.CreateAsync(connection);
            return new TestBackfill(dataSource, connection, schema);
        }

        public async Task<Guid> AddClipAsync(int daysAgo, string videoState = "ready", string videoHost = "bunny")
        {
            Guid clipId = Guid.NewGuid();
            await Connection.ExecuteAsync("""
                INSERT INTO clip (id, owner_id, video_id, game_category_id, title, video_state, video_host, created_at)
                VALUES (@ClipId, @OwnerId, @VideoId, @GameCategoryId, 'Clip', @VideoState, @VideoHost,
                        now() - make_interval(days => @DaysAgo))
                """, new
            {
                ClipId = clipId, OwnerId = Guid.NewGuid(), VideoId = Guid.NewGuid(), GameCategoryId = Guid.NewGuid(),
                VideoState = videoState, VideoHost = videoHost, DaysAgo = daysAgo
            });
            return clipId;
        }

        public async Task AddRunAsync(Guid clipId, string type, string status) =>
            await Connection.ExecuteAsync(
                "INSERT INTO transcode_run (clip_id, type, status) VALUES (@ClipId, @Type, @Status)",
                new { ClipId = clipId, Type = type, Status = status });

        public async ValueTask DisposeAsync()
        {
            try
            {
                await Connection.ExecuteAsync($"DROP SCHEMA {_schema} CASCADE");
            }
            finally
            {
                await Connection.DisposeAsync();
                await _dataSource.DisposeAsync();
            }
        }
    }
}
