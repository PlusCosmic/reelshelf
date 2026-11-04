using Dapper;
using Npgsql;
using Reelshelf.Test.Video;
using Xunit;

namespace Reelshelf.Test.Core;

/// <summary>
/// V59 (#331) against a real Postgres: it normalises rows that would break its constraints rather than failing on
/// them, runs twice, and then refuses the combinations no statement produces.
/// </summary>
public class StateCheckConstraintsTests
{
    [PostgresFact]
    public async Task V59_NormalisesExistingRows_ThenRefusesBadCombinations()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        NpgsqlConnection db = test.Connection;
        Guid owner = Guid.NewGuid();

        Guid openResolved = Guid.NewGuid(), dismissedUnresolved = Guid.NewGuid();
        await db.ExecuteAsync("""
            INSERT INTO content_report (id, status, resolved_at, resolved_by) VALUES
                (@openResolved, 'open', now(), gen_random_uuid()),
                (@dismissedUnresolved, 'dismissed', NULL, NULL)
            """, new { openResolved, dismissedUnresolved });

        Guid runningDone = Guid.NewGuid(), readyBare = Guid.NewGuid(), failedBare = Guid.NewGuid();
        await db.ExecuteAsync("""
            INSERT INTO clip_export (id, user_id, status, error, completed_at, expires_at) VALUES
                (@runningDone, @owner, 'running', NULL, now(), now()),
                (@readyBare, @owner, 'ready', 'old error', NULL, NULL),
                (@failedBare, @owner, 'failed', NULL, NULL, now())
            """, new { runningDone, readyBare, failedBare, owner });

        Guid keptBinned = Guid.NewGuid(), copyUnmigrated = Guid.NewGuid(), bunnyOwnCopy = Guid.NewGuid();
        Guid bunnyOther = Guid.NewGuid();
        await db.ExecuteAsync("""
            INSERT INTO clip (id, owner_id, video_id, game_category_id, video_host, kept_at, binned_at, bunny_video_id, migrated_at) VALUES
                (@keptBinned, @owner, gen_random_uuid(), gen_random_uuid(), 'r2', now(), now(), NULL, NULL),
                (@copyUnmigrated, @owner, gen_random_uuid(), gen_random_uuid(), 'r2', NULL, NULL, gen_random_uuid(), now()),
                (@bunnyOwnCopy, @owner, @bunnyOwnCopy, gen_random_uuid(), 'bunny', NULL, NULL, @bunnyOwnCopy, now())
            """, new { keptBinned, copyUnmigrated, bunnyOwnCopy, owner });

        await test.ApplyV59Async();
        await test.ApplyV59Async();

        Assert.Equal((null, null), await db.QuerySingleAsync<(DateTime?, Guid?)>(
            "SELECT resolved_at, resolved_by FROM content_report WHERE id = @openResolved", new { openResolved }));
        Assert.NotNull(await db.ExecuteScalarAsync<DateTime?>(
            "SELECT resolved_at FROM content_report WHERE id = @dismissedUnresolved", new { dismissedUnresolved }));

        Assert.Equal((null, null), await db.QuerySingleAsync<(DateTime?, DateTime?)>(
            "SELECT completed_at, expires_at FROM clip_export WHERE id = @runningDone", new { runningDone }));
        (DateTime? completed, DateTime? expires, string? error) ready =
            await db.QuerySingleAsync<(DateTime?, DateTime?, string?)>(
                "SELECT completed_at, expires_at, error FROM clip_export WHERE id = @readyBare", new { readyBare });
        Assert.NotNull(ready.completed);
        Assert.NotNull(ready.expires);
        Assert.Null(ready.error);
        Assert.Equal("The export failed", await db.ExecuteScalarAsync<string>(
            "SELECT error FROM clip_export WHERE id = @failedBare AND expires_at IS NULL AND completed_at IS NOT NULL",
            new { failedBare }));

        Assert.Null(await db.ExecuteScalarAsync<DateTime?>(
            "SELECT kept_at FROM clip WHERE id = @keptBinned", new { keptBinned }));
        Assert.Null(await db.ExecuteScalarAsync<Guid?>(
            "SELECT bunny_video_id FROM clip WHERE id = @bunnyOwnCopy", new { bunnyOwnCopy }));

        await AssertRefusedAsync(db, "content_report_resolution_check",
            "UPDATE content_report SET status = 'taken_down' WHERE id = @Id", openResolved);
        await AssertRefusedAsync(db, "clip_export_status_fields_check",
            "UPDATE clip_export SET status = 'ready' WHERE id = @Id", runningDone);
        await AssertRefusedAsync(db, "clip_export_status_fields_check",
            "UPDATE clip_export SET status = 'failed', error = NULL WHERE id = @Id", failedBare);
        await AssertRefusedAsync(db, "clip_kept_not_binned_check",
            "UPDATE clip SET kept_at = now() WHERE id = @Id", keptBinned);
        await AssertRefusedAsync(db, "clip_bunny_copy_migrated_check",
            "UPDATE clip SET migrated_at = NULL WHERE id = @Id", copyUnmigrated);
        await AssertRefusedAsync(db, "clip_bunny_host_no_copy_check",
            "UPDATE clip SET bunny_video_id = gen_random_uuid() WHERE id = @Id", bunnyOwnCopy);

        // A Bunny copy with no migrated_at (a hand edit) is never given one, which would schedule the delete sweep to
        // remove that Bunny video: the constraint waits instead.
        Guid strayCopy = Guid.NewGuid();
        await db.ExecuteAsync("ALTER TABLE clip DROP CONSTRAINT clip_bunny_copy_migrated_check");
        await db.ExecuteAsync("""
            INSERT INTO clip (id, owner_id, video_id, game_category_id, video_host, bunny_video_id, migrated_at)
            VALUES (@strayCopy, @owner, gen_random_uuid(), gen_random_uuid(), 'r2', gen_random_uuid(), NULL)
            """, new { strayCopy, owner });
        await test.ApplyV59Async();
        Assert.Null(await db.ExecuteScalarAsync<DateTime?>(
            "SELECT migrated_at FROM clip WHERE id = @strayCopy", new { strayCopy }));
        Assert.Equal(0, await db.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM pg_constraint WHERE conname = 'clip_bunny_copy_migrated_check'" +
            " AND connamespace = current_schema()::regnamespace"));
        await db.ExecuteAsync("DELETE FROM clip WHERE id = @strayCopy", new { strayCopy });
        await test.ApplyV59Async();

        // A clip on Bunny with another Bunny copy (a hand edit) can't be cleared safely, so the last constraint waits.
        await db.ExecuteAsync("ALTER TABLE clip DROP CONSTRAINT clip_bunny_host_no_copy_check");
        await db.ExecuteAsync("""
            INSERT INTO clip (id, owner_id, video_id, game_category_id, video_host, bunny_video_id, migrated_at)
            VALUES (@bunnyOther, @owner, gen_random_uuid(), gen_random_uuid(), 'bunny', gen_random_uuid(), now())
            """, new { bunnyOther, owner });
        await test.ApplyV59Async();
        Assert.NotNull(await db.ExecuteScalarAsync<Guid?>(
            "SELECT bunny_video_id FROM clip WHERE id = @bunnyOther", new { bunnyOther }));
        Assert.Equal(0, await db.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM pg_constraint WHERE conname = 'clip_bunny_host_no_copy_check'" +
            " AND connamespace = current_schema()::regnamespace"));
    }

    private static async Task AssertRefusedAsync(NpgsqlConnection db, string constraint, string sql, Guid id)
    {
        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync(sql, new { Id = id }));
        Assert.Equal(constraint, refused.ConstraintName);
    }

    private sealed class TestDatabase(NpgsqlConnection connection, string schema) : IAsyncDisposable
    {
        public NpgsqlConnection Connection => connection;

        public static async Task<TestDatabase> CreateAsync()
        {
            string schema = $"state_check_test_{Guid.NewGuid():N}";
            NpgsqlConnection connection = new($"{PostgresFactAttribute.ConnectionString};Search Path={schema}");
            await connection.OpenAsync();
            await connection.ExecuteAsync($"CREATE SCHEMA {schema}");
            await TestClipSchema.CreateAsync(connection);
            // The parts of content_report (V51, V52) and clip_export (V47) that V59 reads.
            await connection.ExecuteAsync("""
                CREATE TABLE content_report (
                    id uuid PRIMARY KEY,
                    status text NOT NULL DEFAULT 'open',
                    created_at timestamptz NOT NULL DEFAULT now(),
                    resolved_by uuid,
                    resolved_at timestamptz
                );
                CREATE TABLE clip_export (
                    id uuid PRIMARY KEY,
                    user_id uuid NOT NULL,
                    status text NOT NULL DEFAULT 'pending',
                    error text,
                    created_at timestamptz NOT NULL DEFAULT now(),
                    completed_at timestamptz,
                    expires_at timestamptz
                );
                """);
            return new TestDatabase(connection, schema);
        }

        public async Task ApplyV59Async() =>
            await connection.ExecuteAsync(
                await File.ReadAllTextAsync(TestClipSchema.MigrationPath("V59__state_check_constraints.sql")));

        public async ValueTask DisposeAsync()
        {
            await connection.ExecuteAsync($"DROP SCHEMA {schema} CASCADE");
            await connection.DisposeAsync();
        }
    }
}
