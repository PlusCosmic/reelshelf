using Dapper;
using Npgsql;
using Reelshelf.Moderation;
using Xunit;

namespace Reelshelf.Test.Moderation;

/// <summary>
/// Runs the V51 migration and <see cref="ContentReportStatements"/> against a real Postgres, in a schema of the test's
/// own holding the tables they need, dropped afterwards.
/// </summary>
public class ContentReportStatementsTests
{
    [PostgresFact]
    public async Task AnUrgentReport_HidesTheShare_UntilItIsDismissed()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        Guid admin = await test.AddUserAsync("admin", "admin@example.com");
        (Guid clip, string token) = await test.AddSharedClipAsync(await test.AddUserAsync("owner", null));

        await test.ReportAsync(clip, token, ContentReportReasons.Sexual);
        Assert.NotNull(await test.Statements.FindShare(token));

        Guid urgent = await test.ReportAsync(clip, token, ContentReportReasons.ChildSafety);
        Assert.Null(await test.Statements.FindShare(token));

        Assert.Equal(ResolveOutcome.Resolved, await test.Statements.Dismiss(urgent, admin, "a game's cutscene"));
        Assert.NotNull(await test.Statements.FindShare(token));
        Assert.Equal(ResolveOutcome.AlreadyResolved, await test.Statements.Dismiss(urgent, admin, null));
        Assert.Equal(ResolveOutcome.NotFound, await test.Statements.Dismiss(Guid.NewGuid(), admin, null));
    }

    [PostgresFact]
    public async Task TakeDown_BlocksSharing_RevokesTheLink_AndResolvesEveryOpenReportOnTheClip()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        Guid admin = await test.AddUserAsync("admin", null);
        (Guid clip, string token) = await test.AddSharedClipAsync(await test.AddUserAsync("owner", "owner@example.com"));
        (Guid otherClip, string otherToken) = await test.AddSharedClipAsync(await test.AddUserAsync("other", null));
        Guid first = await test.ReportAsync(clip, token, ContentReportReasons.Violence);
        Guid second = await test.ReportAsync(clip, token, ContentReportReasons.Other);
        Guid elsewhere = await test.ReportAsync(otherClip, otherToken, ContentReportReasons.Violence);

        TakeDownResult result = await test.Statements.TakeDown(first, admin, "gore");

        Assert.Equal((ResolveOutcome.Resolved, clip, "owner@example.com", ContentReportReasons.Violence),
            (result.Outcome, result.ClipId, result.OwnerEmail, result.Reason));
        Assert.Null(await test.Statements.FindShare(token));
        Assert.True(await test.Connection.QuerySingleAsync<bool>(
            "SELECT sharing_blocked_at IS NOT NULL FROM clip WHERE id = @clip", new { clip }));
        Assert.True(await test.Connection.QuerySingleAsync<bool>(
            "SELECT revoked_at IS NOT NULL FROM clip_share WHERE token = @token", new { token }));

        ContentReportRow resolved = (await test.Statements.Get(second))!;
        Assert.Equal((ContentReportStatuses.TakenDown, "admin", "gore", true),
            (resolved.Status, resolved.ResolvedByName, resolved.ResolutionNote, resolved.SharingBlocked));
        Assert.Equal(ContentReportStatuses.Open, (await test.Statements.Get(elsewhere))!.Status);
        Assert.Equal([elsewhere], (await test.Statements.List(open: true, 10)).Select(row => row.Id));
        Assert.Equal(ResolveOutcome.AlreadyResolved, (await test.Statements.TakeDown(second, admin, "again")).Outcome);
    }

    [PostgresFact]
    public async Task AReport_OutlivesItsClip()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        Guid admin = await test.AddUserAsync("admin", null);
        (Guid clip, string token) = await test.AddSharedClipAsync(await test.AddUserAsync("owner", null));
        Guid report = await test.ReportAsync(clip, token, ContentReportReasons.Copyright);

        await test.Connection.ExecuteAsync("DELETE FROM clip WHERE id = @clip", new { clip });

        ContentReportRow row = (await test.Statements.Get(report))!;
        Assert.Equal((null, "clip", "owner"), (row.ClipId, row.ClipTitle, row.OwnerName));
        TakeDownResult result = await test.Statements.TakeDown(report, admin, "already gone");
        Assert.Equal((ResolveOutcome.Resolved, null), (result.Outcome, result.OwnerEmail));
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly string _schema;

        private TestDatabase(NpgsqlConnection connection, string schema)
        {
            Connection = connection;
            _schema = schema;
            Statements = new ContentReportStatements(connection);
        }

        public NpgsqlConnection Connection { get; }

        public ContentReportStatements Statements { get; }

        public static async Task<TestDatabase> CreateAsync()
        {
            DefaultTypeMap.MatchNamesWithUnderscores = true;
            string schema = $"report_test_{Guid.NewGuid():N}";
            NpgsqlConnection connection = new($"{PostgresFactAttribute.ConnectionString};Search Path={schema}");
            await connection.OpenAsync();
            await connection.ExecuteAsync($"CREATE SCHEMA {schema}");
            // The parts of app_user, clip and clip_share that V51 and the statements read.
            await connection.ExecuteAsync("""
                CREATE TABLE app_user (
                    id uuid PRIMARY KEY,
                    username text NOT NULL,
                    global_name text,
                    email text,
                    role text NOT NULL DEFAULT 'Editor',
                    deletion_requested_at timestamptz
                );
                CREATE TABLE clip (
                    id uuid PRIMARY KEY,
                    owner_id uuid NOT NULL REFERENCES app_user(id) ON DELETE CASCADE,
                    title text
                );
                CREATE TABLE clip_share (
                    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                    token text NOT NULL UNIQUE,
                    clip_id uuid NOT NULL REFERENCES clip(id) ON DELETE CASCADE,
                    owner_id uuid NOT NULL REFERENCES app_user(id) ON DELETE CASCADE,
                    created_at timestamptz NOT NULL DEFAULT now(),
                    revoked_at timestamptz
                );
                """);
            string sql = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "migrations", "db",
                "migrations", "V51__content_reports.sql"));
            await connection.ExecuteAsync(sql);
            return new TestDatabase(connection, schema);
        }

        public async Task<Guid> AddUserAsync(string username, string? email)
        {
            Guid id = Guid.NewGuid();
            await Connection.ExecuteAsync(
                "INSERT INTO app_user (id, username, email) VALUES (@id, @username, @email)",
                new { id, username, email });
            return id;
        }

        public async Task<(Guid Clip, string Token)> AddSharedClipAsync(Guid ownerId)
        {
            Guid clip = Guid.NewGuid();
            string token = Guid.NewGuid().ToString("N");
            await Connection.ExecuteAsync("""
                INSERT INTO clip (id, owner_id, title) VALUES (@clip, @ownerId, 'clip');
                INSERT INTO clip_share (token, clip_id, owner_id) VALUES (@token, @clip, @ownerId);
                """, new { clip, ownerId, token });
            return (clip, token);
        }

        public async Task<Guid> ReportAsync(Guid clip, string token, string reason)
        {
            ReportedShare share = (await Statements.FindShare(token))!;
            Assert.Equal(clip, share.ClipId);
            return await Statements.Insert(new NewContentReport(share.ClipId, share.OwnerId, share.Title, token, reason,
                "", null));
        }

        public async ValueTask DisposeAsync()
        {
            await Connection.ExecuteAsync($"DROP SCHEMA {_schema} CASCADE");
            await Connection.DisposeAsync();
        }

        private static string RepositoryRoot()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Reelshelf.sln")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? throw new InvalidOperationException("Reelshelf.sln not found");
        }
    }
}
