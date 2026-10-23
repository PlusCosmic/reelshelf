using Dapper;
using Npgsql;
using Reelshelf.Core;
using Reelshelf.Plans;
using Reelshelf.Test.Video;
using Xunit;

namespace Reelshelf.Test.Plans;

/// <summary>Runs the grace and export queries against a real Postgres, on the V47 migration itself.</summary>
public class PlanEnforcementStatementsTests
{
    [PostgresFact]
    public async Task GracesPicksAndExports_RoundTrip()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        string schema = $"enforce_test_{Guid.NewGuid():N}";
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
            await connection.ExecuteAsync("""
                CREATE TABLE app_user (id uuid PRIMARY KEY, email text, deletion_requested_at timestamptz);
                CREATE TABLE game_category (id uuid PRIMARY KEY, name text NOT NULL);
                """);
            await TestClipSchema.CreateAsync(connection);
            await connection.ExecuteAsync(
                await File.ReadAllTextAsync(TestClipSchema.MigrationPath("V47__plan_enforcement.sql")));
            await AccountsAsync(connection, new PlanEnforcementStatements(dataSource.CreateConnection()));
            await ExportsAsync(connection, new ClipExportStatements(dataSource.CreateConnection()));
        }
        finally
        {
            await connection.ExecuteAsync($"DROP SCHEMA {schema} CASCADE");
        }
    }

    private static async Task AccountsAsync(NpgsqlConnection connection, PlanEnforcementStatements statements)
    {
        Guid busy = Guid.NewGuid(), quiet = Guid.NewGuid(), leaving = Guid.NewGuid();
        Guid game = Guid.NewGuid();
        await connection.ExecuteAsync("""
            INSERT INTO app_user (id, email, deletion_requested_at) VALUES
                (@busy, 'busy@example.com', NULL), (@quiet, NULL, NULL), (@leaving, NULL, now());
            INSERT INTO game_category (id, name) VALUES (@game, 'Apex Legends');
            """, new { busy, quiet, leaving, game });
        Guid first = await InsertClip(connection, busy, game, DateTimeOffset.Parse("2026-10-01T00:00:00Z"));
        await InsertClip(connection, busy, game, DateTimeOffset.Parse("2026-10-02T00:00:00Z"));
        await InsertClip(connection, busy, game, DateTimeOffset.Parse("2026-10-03T00:00:00Z"));
        await InsertClip(connection, leaving, game, DateTimeOffset.Parse("2026-10-03T00:00:00Z"));
        await InsertClip(connection, leaving, game, DateTimeOffset.Parse("2026-10-03T00:00:00Z"));
        await InsertClip(connection, leaving, game, DateTimeOffset.Parse("2026-10-03T00:00:00Z"));

        // Over two clips: only the busy account (the leaving one is being deleted).
        Assert.Equal([busy], await statements.GetAccountsToCheck(2));

        Assert.True(await statements.StartGrace(busy));
        Assert.False(await statements.StartGrace(busy));
        Assert.True(await statements.MarkGraceWarned(busy));
        Assert.False(await statements.MarkGraceWarned(busy));
        OverLimitState state = await statements.GetState(busy);
        Assert.Equal("busy@example.com", state.Email);
        Assert.NotNull(state.OverLimitSince);
        Assert.NotNull(state.WarnedAt);

        // A grace keeps the account under watch even once it has few clips.
        Assert.Contains(busy, await statements.GetAccountsToCheck(10));
        Assert.False(await statements.MarkGraceWarned(quiet));
        await statements.EndGrace(busy);
        Assert.Equal(new OverLimitState("busy@example.com", null, null), await statements.GetState(busy));

        Assert.True(await statements.SetKept(busy, first, keep: true));
        Assert.False(await statements.SetKept(quiet, first, keep: true));
        List<OwnedClip> clips = await statements.GetOwnedClips(busy);
        Assert.Equal(3, clips.Count);
        OwnedClip picked = Assert.Single(clips, clip => clip.KeptAt is not null);
        Assert.Equal((first, "Apex Legends"), (picked.Id, picked.GameName));
        Assert.True(await statements.SetKept(busy, first, keep: false));
        Assert.All(await statements.GetOwnedClips(busy), clip => Assert.Null(clip.KeptAt));
    }

    private static async Task ExportsAsync(NpgsqlConnection connection, ClipExportStatements statements)
    {
        Guid owner = await connection.QuerySingleAsync<Guid>("SELECT id FROM app_user WHERE email = 'busy@example.com'");
        List<Guid> clipIds = (await connection.QueryAsync<Guid>(
            "SELECT id FROM clip WHERE owner_id = @owner ORDER BY created_at", new { owner })).ToList();

        Guid exportId = await statements.Create(owner, ClipExportReasons.GraceEnded, clipIds.Take(2).ToList());
        ClipExport created = (await statements.Get(exportId))!;
        Assert.Equal((ClipExportStatuses.Pending, 0), (created.Status, created.Attempts));
        Assert.Equal(clipIds.Take(2), created.ClipIds);

        ClipExport claimed = (await statements.Claim(TimeSpan.FromMinutes(30)))!;
        Assert.Equal((exportId, ClipExportStatuses.Running, 1), (claimed.Id, claimed.Status, claimed.Attempts));
        Assert.Null(await statements.Claim(TimeSpan.FromMinutes(30)));

        // Gone quiet: claimed again.
        await connection.ExecuteAsync("UPDATE clip_export SET heartbeat_at = now() - interval '1 hour'");
        Assert.Equal(2, (await statements.Claim(TimeSpan.FromMinutes(30)))!.Attempts);

        // A retry waits out its delay.
        await statements.Fail(exportId, "storage is down", TimeSpan.FromMinutes(10));
        Assert.Equal((ClipExportStatuses.Pending, "storage is down"),
            ((await statements.Get(exportId))!.Status, (await statements.Get(exportId))!.Error));
        Assert.Null(await statements.Claim(TimeSpan.FromMinutes(30)));
        await connection.ExecuteAsync("UPDATE clip_export SET available_at = now()");
        Assert.NotNull(await statements.Claim(TimeSpan.FromMinutes(30)));

        List<ExportClipRow> rows = await statements.GetExportClips(owner, clipIds.Take(2).Append(Guid.NewGuid()).ToList());
        Assert.Equal(clipIds.Take(2), rows.Select(row => row.Id));
        Assert.Equal("Apex Legends", rows[0].GameName);

        await statements.Complete(exportId, [new ClipExportPart(1, 1234, 2)], DateTimeOffset.UtcNow.AddDays(28));
        ClipExport ready = (await statements.Get(exportId))!;
        Assert.Equal(ClipExportStatuses.Ready, ready.Status);
        Assert.Equal([new ClipExportPart(1, 1234, 2)], ready.Parts);
        Assert.Null(ready.Error);
        Assert.Empty(await statements.ExpireDue());

        await connection.ExecuteAsync("UPDATE clip_export SET expires_at = now() - interval '1 minute'");
        Assert.Equal([exportId], (await statements.ExpireDue()).Select(export => export.Id));
        Assert.Equal(ClipExportStatuses.Expired, (await statements.Get(exportId))!.Status);

        Guid failing = await statements.Create(owner, ClipExportReasons.Requested, [clipIds[0]]);
        await statements.Claim(TimeSpan.FromMinutes(30));
        await statements.Fail(failing, "no", retryAfter: null);
        Assert.Equal(ClipExportStatuses.Failed, (await statements.Get(failing))!.Status);
        Assert.Equal([failing, exportId], (await statements.GetForUser(owner)).Select(export => export.Id));
    }

    private static async Task<Guid> InsertClip(NpgsqlConnection connection, Guid owner, Guid game, DateTimeOffset createdAt)
    {
        Guid id = Guid.NewGuid();
        await connection.ExecuteAsync("""
            INSERT INTO clip (id, owner_id, video_id, game_category_id, created_at, title, storage_size, video_state)
            VALUES (@id, @owner, @id, @game, @createdAt, 'clip', 1000, 'ready')
            """, new { id, owner, game, createdAt });
        return id;
    }
}
