using Dapper;
using Npgsql;
using Reelshelf.Auth;
using Reelshelf.Billing;
using Xunit;

namespace Reelshelf.Test.Auth;

/// <summary>
/// Runs the V50 migration and <see cref="AccountAccessStatements"/> against a real Postgres, in a schema of the test's
/// own holding the tables they need, dropped afterwards.
/// </summary>
public class AccountAccessStatementsTests
{
    // An entry in the whitelist V50 carries across, pinned to Admin, and one that isn't pinned.
    private const string WhitelistedAdminDiscordId = "201311542791241728";
    private const string WhitelistedDiscordId = "620243863671537676";

    [PostgresFact]
    public async Task TheMigration_GrantsWhitelistedAccountsUnlimited_AndTheirPinnedRoles()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        Guid admin = await test.AddUserAsync("admin", "Admin", ("discord", WhitelistedAdminDiscordId));
        Guid listed = await test.AddUserAsync("listed", "Editor", ("discord", WhitelistedDiscordId),
            ("twitch", "999"));
        Guid extra = await test.AddUserAsync("extra", "Editor");
        await test.Connection.ExecuteAsync(
            "INSERT INTO user_additional_permission (user_id, permission) VALUES (@extra, 'admin.users')",
            new { extra });
        // A Twitch id equal to a whitelisted Discord id is a different identity.
        Guid stranger = await test.AddUserAsync("stranger", "Editor", ("twitch", WhitelistedAdminDiscordId));

        await test.MigrateAsync();

        Assert.Equal(("Admin", "unlimited"), await test.AccessAsync(admin));
        Assert.Equal(("Editor", "unlimited"), await test.AccessAsync(listed));
        Assert.Equal(("Admin", (string?)null), await test.AccessAsync(extra));
        Assert.Equal(("Editor", (string?)null), await test.AccessAsync(stranger));

        List<AccountAccessChange> adminChanges = await test.Statements.GetChanges(admin);
        Assert.Equal(["granted_tier:unlimited:whitelist.json", "role:Admin:whitelist.json"],
            adminChanges.Select(change => $"{change.Field}:{change.NewValue}:{change.Reason}").Order());
        Assert.All(adminChanges, change => Assert.Null(change.ChangedBy));
        Assert.Equal("user_additional_permission", Assert.Single(await test.Statements.GetChanges(extra)).Reason);
        Assert.Empty(await test.Statements.GetChanges(stranger));
    }

    [PostgresFact]
    public async Task SetRole_RecordsTheChange_AndKeepsTheLastAdmin()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        await test.MigrateAsync();
        Guid first = await test.AddUserAsync("first", "Admin");
        Guid second = await test.AddUserAsync("second", "Editor");

        Assert.Equal(AccessChangeOutcome.LastAdmin,
            await test.Statements.SetRole(first, UserRole.Editor, second, "test"));
        Assert.Equal(AccessChangeOutcome.Changed,
            await test.Statements.SetRole(second, UserRole.Admin, first, "helps run the site"));
        Assert.Equal(AccessChangeOutcome.Unchanged,
            await test.Statements.SetRole(second, UserRole.Admin, first, "again"));
        Assert.Equal(AccessChangeOutcome.Changed,
            await test.Statements.SetRole(first, UserRole.Editor, second, "stepping down"));

        Assert.Equal("Editor", (await test.AccessAsync(first)).Role);
        Assert.Equal("Admin", (await test.AccessAsync(second)).Role);
        AccountAccessChange promoted = Assert.Single(await test.Statements.GetChanges(second));
        Assert.Equal(("role", "Editor", "Admin", "helps run the site", first, "first"),
            (promoted.Field, promoted.OldValue, promoted.NewValue, promoted.Reason, promoted.ChangedBy,
                promoted.ChangedByName));
    }

    [PostgresFact]
    public async Task AnAccountBeingDeleted_IsGone_AndNoLongerCountsAsAnAdmin()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        await test.MigrateAsync();
        Guid admin = await test.AddUserAsync("admin", "Admin");
        Guid leaving = await test.AddUserAsync("leaving", "Admin");
        await test.Connection.ExecuteAsync("UPDATE app_user SET deletion_requested_at = now() WHERE id = @leaving",
            new { leaving });

        Assert.Equal(AccessChangeOutcome.NotFound,
            await test.Statements.SetGrantedTier(leaving, AccountTier.Pro, admin, "test"));
        Assert.Null(await test.Statements.GetAccount(leaving));
        Assert.Equal(AccessChangeOutcome.LastAdmin,
            await test.Statements.SetRole(admin, UserRole.Editor, leaving, "test"));
    }

    [PostgresFact]
    public async Task SetGrantedTier_SetsAndClears_AndCountsForTierQueries()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        await test.MigrateAsync();
        Guid admin = await test.AddUserAsync("admin", "Admin");
        Guid friend = await test.AddUserAsync("friend", "Editor");
        Guid tester = await test.AddUserAsync("tester", "Editor");

        Assert.Equal(AccessChangeOutcome.Changed,
            await test.Statements.SetGrantedTier(friend, AccountTier.Plus, admin, "friend"));
        Assert.Equal(AccessChangeOutcome.Changed,
            await test.Statements.SetGrantedTier(tester, AccountTier.Pro, admin, "testing transcripts"));
        Assert.Equal(AccessChangeOutcome.Unchanged,
            await test.Statements.SetGrantedTier(tester, AccountTier.Pro, admin, "again"));

        Assert.Equal(AccountTier.Plus, await test.Statements.GetGrantedTier(friend));
        Assert.Equal([tester], await test.Statements.GetUserIdsWithGrantedTierAtLeast(AccountTier.Pro));

        Assert.Equal(AccessChangeOutcome.Changed,
            await test.Statements.SetGrantedTier(tester, null, admin, "done testing"));
        Assert.Null(await test.Statements.GetGrantedTier(tester));
        Assert.Equal(["pro→", "→pro"],
            (await test.Statements.GetChanges(tester)).Select(change => $"{change.OldValue}→{change.NewValue}"));
    }

    [PostgresFact]
    public async Task FindAccounts_MatchesNamesAndExactProviderIds()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        await test.MigrateAsync();
        Guid harry = await test.AddUserAsync("harry_cosmic", "Editor", ("discord", "1234567890"),
            ("twitch", "555"));
        await test.AddUserAsync("someone", "Editor", ("discord", "1234567891"));

        AccountAccessRow byName = Assert.Single(await test.Statements.FindAccounts("COSMIC", 20));
        Assert.Equal(harry, byName.Id);
        Assert.Equal(["discord", "twitch"], byName.Identities.Select(identity => identity.Provider));
        Assert.Equal(harry, Assert.Single(await test.Statements.FindAccounts("555", 20)).Id);
        // Ids match exactly, and an underscore is a literal.
        Assert.Empty(await test.Statements.FindAccounts("123456789", 20));
        Assert.Equal(harry, Assert.Single(await test.Statements.FindAccounts("y_c", 20)).Id);
        Assert.Empty(await test.Statements.FindAccounts("y%c", 20));
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly string _schema;

        private TestDatabase(NpgsqlConnection connection, string schema)
        {
            Connection = connection;
            _schema = schema;
            Statements = new AccountAccessStatements(connection);
        }

        public NpgsqlConnection Connection { get; }

        public AccountAccessStatements Statements { get; }

        public static async Task<TestDatabase> CreateAsync()
        {
            DefaultTypeMap.MatchNamesWithUnderscores = true;
            string schema = $"access_test_{Guid.NewGuid():N}";
            NpgsqlConnection connection = new($"{PostgresFactAttribute.ConnectionString};Search Path={schema}");
            await connection.OpenAsync();
            await connection.ExecuteAsync($"CREATE SCHEMA {schema}");
            // The parts of app_user, user_identity and user_additional_permission that V50 and the statements read.
            await connection.ExecuteAsync("""
                CREATE TABLE app_user (
                    id uuid PRIMARY KEY,
                    username text NOT NULL,
                    global_name text,
                    avatar_url text,
                    role text NOT NULL DEFAULT 'Editor',
                    role_from_whitelist boolean NOT NULL DEFAULT false,
                    deletion_requested_at timestamptz,
                    -- V52's; the statements read it.
                    suspended_at timestamptz
                );
                CREATE TABLE user_identity (
                    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                    user_id uuid NOT NULL REFERENCES app_user(id) ON DELETE CASCADE,
                    provider text NOT NULL,
                    provider_user_id text NOT NULL,
                    username text NOT NULL,
                    linked_at timestamptz NOT NULL DEFAULT clock_timestamp()
                );
                CREATE TABLE user_additional_permission (
                    user_id uuid NOT NULL REFERENCES app_user(id) ON DELETE CASCADE,
                    permission text NOT NULL
                );
                """);
            return new TestDatabase(connection, schema);
        }

        /// <summary>Runs V50 as Evolve does, in one transaction.</summary>
        public async Task MigrateAsync()
        {
            string sql = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "migrations", "db",
                "migrations", "V50__database_backed_access.sql"));
            await using NpgsqlTransaction transaction = await Connection.BeginTransactionAsync();
            await Connection.ExecuteAsync(sql, transaction: transaction);
            await transaction.CommitAsync();
        }

        public async Task<Guid> AddUserAsync(string username, string role,
            params (string Provider, string ProviderUserId)[] identities)
        {
            Guid id = Guid.NewGuid();
            await Connection.ExecuteAsync("INSERT INTO app_user (id, username, role) VALUES (@id, @username, @role)",
                new { id, username, role });
            foreach ((string provider, string providerUserId) in identities)
            {
                await Connection.ExecuteAsync("""
                    INSERT INTO user_identity (user_id, provider, provider_user_id, username)
                    VALUES (@id, @provider, @providerUserId, @username)
                    """, new { id, provider, providerUserId, username });
            }

            return id;
        }

        public async Task<(string Role, string? GrantedTier)> AccessAsync(Guid userId)
        {
            return await Connection.QuerySingleAsync<(string, string?)>(
                "SELECT role, granted_tier FROM app_user WHERE id = @userId", new { userId });
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
