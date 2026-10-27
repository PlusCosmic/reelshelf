using System.Text.Json;
using Dapper;
using Npgsql;
using Reelshelf.Auth;
using Reelshelf.Moderation;
using Xunit;

namespace Reelshelf.Test.Moderation;

/// <summary>
/// Runs the V51 and V52 migrations and <see cref="ContentReportStatements"/> against a real Postgres, in a schema of the test's
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
        ContentReportRow hidden = (await test.Statements.Get(urgent))!;
        Assert.Equal((null, true), (hidden.ActiveShareToken, hidden.ShareHidden));

        Assert.Equal(ResolveOutcome.Resolved, await test.Statements.Dismiss(urgent, admin, "a game's cutscene"));
        Assert.NotNull(await test.Statements.FindShare(token));
        ContentReportRow shown = (await test.Statements.Get(urgent))!;
        Assert.Equal((token, false), (shown.ActiveShareToken, shown.ShareHidden));
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
        Assert.Equal((ContentReportStatuses.TakenDown, "admin", "gore", true, false),
            (resolved.Status, resolved.ResolvedByName, resolved.ResolutionNote, resolved.SharingBlocked,
                resolved.ShareHidden));
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

    [PostgresFact]
    public async Task Remove_KeepsEvidence_SuspendsTheOwner_AndDeletesTheClip()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        Guid admin = await test.AddUserAsync("admin", null, "Admin");
        Guid owner = await test.AddUserAsync("owner", "owner@example.com");
        await test.Connection.ExecuteAsync("""
            INSERT INTO user_identity (user_id, provider, provider_user_id, username, email)
            VALUES (@owner, 'discord', '1234', 'owner#1', 'owner@example.com')
            """, new { owner });
        (Guid clip, string token) = await test.AddSharedClipAsync(owner);
        (Guid otherClip, string otherToken) = await test.AddSharedClipAsync(owner);
        Guid first = await test.ReportAsync(clip, token, ContentReportReasons.Violence);
        Guid urgent = await test.ReportAsync(clip, token, ContentReportReasons.ChildSafety);

        RemoveResult result = await test.Statements.Remove(urgent, admin, "reported to the IWF", TimeSpan.FromDays(365));

        Assert.Equal((RemoveOutcome.Removed, clip, true), (result.Outcome, result.ClipId, result.OwnerSuspended));
        Assert.False(await test.Connection.QuerySingleAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM clip WHERE id = @clip)", new { clip }));

        ClipEvidence evidence = await test.Connection.QuerySingleAsync<ClipEvidence>(
            "SELECT id, clip_id, owner_id, reason, note, clip::text AS clip, owner::text AS owner, status, delete_after " +
            "FROM clip_evidence WHERE id = @id", new { id = result.EvidenceId });
        Assert.Equal((clip, owner, ContentReportReasons.ChildSafety, ClipEvidenceStatuses.Pending),
            (evidence.ClipId, evidence.OwnerId, evidence.Reason, evidence.Status));
        EvidenceClip snapshot = evidence.ReadClip();
        Assert.Equal((clip, "clip", "Apex Legends", "r2", 3), (snapshot.Id, snapshot.Title, snapshot.GameName,
            snapshot.VideoHost, snapshot.VideoVersion));
        using JsonDocument ownerSnapshot = JsonDocument.Parse(evidence.Owner);
        Assert.Equal("owner@example.com", ownerSnapshot.RootElement.GetProperty("email").GetString());
        Assert.Equal("1234", ownerSnapshot.RootElement.GetProperty("identities")[0]
            .GetProperty("provider_user_id").GetString());

        foreach (Guid report in new[] { first, urgent })
        {
            ContentReportRow row = (await test.Statements.Get(report))!;
            Assert.Equal((ContentReportStatuses.Removed, true, ClipEvidenceStatuses.Pending),
                (row.Status, row.OwnerSuspended, row.EvidenceStatus));
        }

        Assert.NotNull(await test.Connection.QuerySingleAsync<DateTime?>(
            "SELECT suspended_at FROM app_user WHERE id = @owner", new { owner }));
        Assert.Equal("suspended", await test.Connection.QuerySingleAsync<string>(
            "SELECT new_value FROM account_access_change WHERE user_id = @owner", new { owner }));
        // The owner's other clip stays, but nobody can watch it through its link while they are suspended.
        Assert.Null(await test.Statements.FindShare(otherToken));
        Assert.Equal(1, await test.Connection.QuerySingleAsync<int>(
            "SELECT count(*)::int FROM clip WHERE id = @otherClip", new { otherClip }));
    }

    [PostgresFact]
    public async Task AnEvidenceRecord_IsClaimed_Retried_Preserved_Swept_AndExpired()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        Guid admin = await test.AddUserAsync("admin", null, "Admin");
        (Guid clip, string token) = await test.AddSharedClipAsync(await test.AddUserAsync("owner", null));
        Guid report = await test.ReportAsync(clip, token, ContentReportReasons.ChildSafety);
        Guid id = (await test.Statements.Remove(report, admin, "x", TimeSpan.FromDays(365))).EvidenceId!.Value;
        ClipEvidenceStatements evidence = new(test.Connection);
        TimeSpan stale = TimeSpan.FromMinutes(30);

        ClipEvidence claimed = (await evidence.Claim(stale, maxAttempts: 2))!;
        Assert.Equal((id, 1, clip), (claimed.Id, claimed.Attempts, claimed.ReadClip().Id));
        Assert.Null(await evidence.Claim(stale, maxAttempts: 2));

        await evidence.SaveFiles(id, [$"{id}/record.json"]);
        await evidence.Fail(id, "Bunny is down", TimeSpan.Zero);
        ClipEvidence retried = (await evidence.Claim(stale, maxAttempts: 2))!;
        Assert.Equal((2, $"[\"{id}/record.json\"]"), (retried.Attempts, retried.Files));

        await evidence.MarkMediaDeleted(id);
        await evidence.SaveCdnPurge(id, CdnPurgeResults.Purged);
        await evidence.MarkPreserved(id);
        ContentReportRow row = (await test.Statements.Get(report))!;
        Assert.Equal((ClipEvidenceStatuses.Preserved, CdnPurgeResults.Purged, (string?)null),
            (row.EvidenceStatus, row.EvidenceCdnPurge, row.EvidenceError));

        Assert.Null(await evidence.ClaimSweep(TimeSpan.FromHours(3), stale));
        Assert.Equal(id, (await evidence.ClaimSweep(TimeSpan.Zero, stale))!.Id);
        await evidence.MarkSwept(id);
        Assert.Null(await evidence.ClaimSweep(TimeSpan.Zero, stale));

        Assert.Empty(await evidence.GetExpired(10));
        await test.Connection.ExecuteAsync("UPDATE clip_evidence SET delete_after = now() - interval '1 day'");
        Assert.Equal([id], await evidence.GetExpired(10));
        await evidence.Delete(id);
        Assert.Null((await test.Statements.Get(report))!.EvidenceStatus);
    }

    [PostgresFact]
    public async Task AnEvidenceRecord_OutOfAttempts_Fails()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        Guid admin = await test.AddUserAsync("admin", null, "Admin");
        (Guid clip, string token) = await test.AddSharedClipAsync(await test.AddUserAsync("owner", null));
        Guid report = await test.ReportAsync(clip, token, ContentReportReasons.Terrorism);
        Guid id = (await test.Statements.Remove(report, admin, "x", TimeSpan.FromDays(365))).EvidenceId!.Value;
        ClipEvidenceStatements evidence = new(test.Connection);

        await evidence.Claim(TimeSpan.FromMinutes(30), maxAttempts: 1);
        await evidence.Fail(id, "storage is down", TimeSpan.Zero);

        Assert.Null(await evidence.Claim(TimeSpan.FromMinutes(30), maxAttempts: 1));
        ContentReportRow row = (await test.Statements.Get(report))!;
        Assert.Equal((ClipEvidenceStatuses.Failed, "storage is down"), (row.EvidenceStatus, row.EvidenceError));
    }

    [PostgresFact]
    public async Task AClipUnderAnOpenUrgentReport_IsHeld_AsIsItsOwner()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        Guid admin = await test.AddUserAsync("admin", null, "Admin");
        Guid owner = await test.AddUserAsync("owner", null);
        (Guid clip, string token) = await test.AddSharedClipAsync(owner);
        (Guid otherClip, _) = await test.AddSharedClipAsync(owner);

        await test.ReportAsync(clip, token, ContentReportReasons.Copyright);
        Assert.False(await test.Statements.IsHeldForReview(clipId: clip));

        Guid urgent = await test.ReportAsync(clip, token, ContentReportReasons.Terrorism);
        Assert.True(await test.Statements.IsHeldForReview(clipId: clip));
        Assert.False(await test.Statements.IsHeldForReview(clipId: otherClip));
        Assert.True(await test.Statements.IsHeldForReview(ownerId: owner));
        Assert.False(await test.Statements.IsHeldForReview(ownerId: admin));

        await test.Statements.Dismiss(urgent, admin, "gameplay");
        Assert.False(await test.Statements.IsHeldForReview(ownerId: owner));
    }

    [PostgresFact]
    public async Task OnlyUrgentReports_RemoveAClip_AndThoseCantBeTakenDown()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        Guid admin = await test.AddUserAsync("admin", null, "Admin");
        (Guid clip, string token) = await test.AddSharedClipAsync(await test.AddUserAsync("owner", null));
        Guid violence = await test.ReportAsync(clip, token, ContentReportReasons.Violence);
        Guid terrorism = await test.ReportAsync(clip, token, ContentReportReasons.Terrorism);

        Assert.Equal(RemoveOutcome.NotUrgent,
            (await test.Statements.Remove(violence, admin, "x", TimeSpan.FromDays(1))).Outcome);
        Assert.Equal(ResolveOutcome.NeedsRemoval, (await test.Statements.TakeDown(terrorism, admin, "x")).Outcome);
        Assert.Equal(ContentReportStatuses.Open, (await test.Statements.Get(terrorism))!.Status);
    }

    [PostgresFact]
    public async Task RemovingAnAdminsClip_KeepsTheEvidence_ButDoesntSuspendThem()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        Guid admin = await test.AddUserAsync("admin", null, "Admin");
        Guid otherAdmin = await test.AddUserAsync("other", null, "Admin");
        (Guid clip, string token) = await test.AddSharedClipAsync(otherAdmin);
        Guid report = await test.ReportAsync(clip, token, ContentReportReasons.Terrorism);

        RemoveResult result = await test.Statements.Remove(report, admin, "x", TimeSpan.FromDays(1));

        Assert.Equal((RemoveOutcome.Removed, false), (result.Outcome, result.OwnerSuspended));
        Assert.Null(await test.Connection.QuerySingleAsync<DateTime?>(
            "SELECT suspended_at FROM app_user WHERE id = @otherAdmin", new { otherAdmin }));
    }

    [PostgresFact]
    public async Task LiftingASuspension_IsRecorded_AndBringsTheLinksBack()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        Guid admin = await test.AddUserAsync("admin", null, "Admin");
        Guid owner = await test.AddUserAsync("owner", null);
        (_, string token) = await test.AddSharedClipAsync(owner);
        AccountAccessStatements access = new(test.Connection);

        Assert.Equal(AccessChangeOutcome.Changed, await access.SetSuspended(owner, true, admin, "spam"));
        Assert.Null(await test.Statements.FindShare(token));
        Assert.Equal(AccessChangeOutcome.Unchanged, await access.SetSuspended(owner, true, admin, "again"));
        Assert.Equal(AccessChangeOutcome.IsAdmin, await access.SetSuspended(admin, true, owner, "x"));
        Assert.Equal(AccessChangeOutcome.Changed, await access.SetSuspended(owner, false, admin, "appeal upheld"));
        Assert.NotNull(await test.Statements.FindShare(token));

        Assert.Equal(["(none)→suspended: spam", "suspended→(none): appeal upheld"],
            (await access.GetChanges(owner))
            .Select(change => $"{change.OldValue ?? "(none)"}→{change.NewValue ?? "(none)"}: {change.Reason}")
            .Order(StringComparer.Ordinal));
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
            // The parts of app_user, user_identity, account_access_change (as V50 made it), game_category, clip and
            // clip_share that V51, V52 and the statements read.
            await connection.ExecuteAsync("""
                CREATE TABLE app_user (
                    id uuid PRIMARY KEY,
                    username text NOT NULL,
                    global_name text,
                    avatar_url text,
                    email text,
                    role text NOT NULL DEFAULT 'Editor',
                    granted_tier text,
                    deletion_requested_at timestamptz
                );
                CREATE TABLE user_identity (
                    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                    user_id uuid NOT NULL REFERENCES app_user(id) ON DELETE CASCADE,
                    provider text NOT NULL,
                    provider_user_id text NOT NULL,
                    username text NOT NULL,
                    display_name text,
                    email text,
                    linked_at timestamptz NOT NULL DEFAULT clock_timestamp()
                );
                CREATE TABLE account_access_change (
                    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                    user_id uuid NOT NULL REFERENCES app_user(id) ON DELETE CASCADE,
                    changed_by uuid REFERENCES app_user(id) ON DELETE SET NULL,
                    field text NOT NULL CONSTRAINT account_access_change_field_check CHECK (field IN ('role', 'granted_tier')),
                    old_value text,
                    new_value text,
                    reason text NOT NULL,
                    changed_at timestamptz NOT NULL DEFAULT clock_timestamp()
                );
                CREATE TABLE game_category (id uuid PRIMARY KEY, name text NOT NULL);
                INSERT INTO game_category VALUES ('00000000-0000-0000-0000-00000000a9e1', 'Apex Legends');
                CREATE TABLE clip (
                    id uuid PRIMARY KEY,
                    owner_id uuid NOT NULL REFERENCES app_user(id) ON DELETE CASCADE,
                    title text,
                    game_category_id uuid REFERENCES game_category(id),
                    video_host text NOT NULL DEFAULT 'r2',
                    video_id uuid,
                    video_version integer NOT NULL DEFAULT 3,
                    video_state text NOT NULL DEFAULT 'ready',
                    bunny_video_id uuid,
                    created_at timestamptz NOT NULL DEFAULT now()
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
            foreach (string migration in new[] { "V51__content_reports.sql", "V52__clip_removal_and_suspension.sql" })
            {
                await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "migrations",
                    "db", "migrations", migration)));
            }
            return new TestDatabase(connection, schema);
        }

        public async Task<Guid> AddUserAsync(string username, string? email, string role = "Editor")
        {
            Guid id = Guid.NewGuid();
            await Connection.ExecuteAsync(
                "INSERT INTO app_user (id, username, email, role) VALUES (@id, @username, @email, @role)",
                new { id, username, email, role });
            return id;
        }

        public async Task<(Guid Clip, string Token)> AddSharedClipAsync(Guid ownerId)
        {
            Guid clip = Guid.NewGuid();
            string token = Guid.NewGuid().ToString("N");
            await Connection.ExecuteAsync("""
                INSERT INTO clip (id, owner_id, title, game_category_id, video_id)
                VALUES (@clip, @ownerId, 'clip', '00000000-0000-0000-0000-00000000a9e1', @clip);
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
