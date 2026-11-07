using Dapper;
using Npgsql;
using Reelshelf.Auth;
using Reelshelf.Billing;
using Reelshelf.FeatureFlags;
using Xunit;

namespace Reelshelf.Test.FeatureFlags;

/// <summary>
/// Runs the V64 migration and <see cref="PostHogPersonStatements"/> against a real Postgres, in a schema of the test's
/// own holding the part of <c>app_user</c> they read, dropped afterwards.
/// </summary>
public class PostHogPersonStatementsTests
{
    [PostgresFact]
    public async Task ReadsLiveAccountsAndRecordsWhatWasSent()
    {
        await using NpgsqlConnection connection = await CreateSchemaAsync();
        PostHogPersonStatements statements = new(connection);
        Guid ada = await AddUserAsync(connection, "Admin", null);
        Guid ben = await AddUserAsync(connection, "Editor", "pro");
        Guid leaving = await AddUserAsync(connection, "Editor", null, deleting: true);

        List<PostHogPersonAccount> accounts = await statements.GetAccounts();

        Assert.Equal(2, accounts.Count);
        Assert.Contains(new PostHogPersonAccount(ada, UserRole.Admin, null), accounts);
        Assert.Contains(new PostHogPersonAccount(ben, UserRole.Editor, AccountTier.Pro), accounts);
        Assert.DoesNotContain(accounts, account => account.Id == leaving);

        await statements.MarkSynced([(ada, "{\"role\":\"admin\"}"), (Guid.NewGuid(), "{}")]);
        await statements.MarkSynced([(ada, "{\"role\":\"editor\"}"), (ben, "{\"role\":\"editor\"}")]);

        Dictionary<Guid, string> synced = await statements.GetSynced();
        // An account that doesn't exist is skipped, and a second send replaces the first.
        Assert.Equal(2, synced.Count);
        Assert.Equal("{\"role\":\"editor\"}", synced[ada]);

        await connection.ExecuteAsync("DELETE FROM app_user WHERE id = @ada", new { ada });
        Assert.False((await statements.GetSynced()).ContainsKey(ada));
    }

    private static async Task<NpgsqlConnection> CreateSchemaAsync()
    {
        string schema = $"posthog_person_test_{Guid.NewGuid():N}";
        NpgsqlConnection connection = new($"{PostgresFactAttribute.ConnectionString};Search Path={schema}");
        await connection.OpenAsync();
        await connection.ExecuteAsync($"CREATE SCHEMA {schema}");
        await connection.ExecuteAsync("""
            CREATE TABLE app_user (
                id uuid PRIMARY KEY,
                role text NOT NULL DEFAULT 'Editor',
                granted_tier text,
                deletion_requested_at timestamptz
            );
            """);
        await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "migrations", "db",
            "migrations", "V64__posthog_person.sql")));
        return new DroppingConnection(connection, schema).Connection;
    }

    private static async Task<Guid> AddUserAsync(NpgsqlConnection connection, string role, string? grantedTier,
        bool deleting = false)
    {
        Guid id = Guid.NewGuid();
        await connection.ExecuteAsync("""
            INSERT INTO app_user (id, role, granted_tier, deletion_requested_at)
            VALUES (@id, @role, @grantedTier, CASE WHEN @deleting THEN now() END)
            """, new { id, role, grantedTier, deleting });
        return id;
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Reelshelf.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Reelshelf.sln not found.");
    }

    /// <summary>Drops the test's schema when the connection is closed.</summary>
    private sealed class DroppingConnection
    {
        public DroppingConnection(NpgsqlConnection connection, string schema)
        {
            Connection = connection;
            connection.StateChange += (_, change) =>
            {
                if (change.CurrentState == System.Data.ConnectionState.Closed)
                {
                    using NpgsqlConnection admin = new(PostgresFactAttribute.ConnectionString);
                    admin.Open();
                    admin.Execute($"DROP SCHEMA IF EXISTS {schema} CASCADE");
                }
            };
        }

        public NpgsqlConnection Connection { get; }
    }
}
