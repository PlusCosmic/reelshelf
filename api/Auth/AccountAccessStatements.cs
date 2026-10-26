using Dapper;
using Npgsql;
using Reelshelf.Billing;

namespace Reelshelf.Auth;

/// <summary>What changing a role or granted tier did.</summary>
public enum AccessChangeOutcome
{
    Changed,
    Unchanged,
    NotFound,

    /// <summary>The change would leave no Admin.</summary>
    LastAdmin
}

/// <summary>One recorded change to an account's role or granted tier (ADR-0008).</summary>
public sealed record AccountAccessChange(
    Guid Id,
    string Field,
    string? OldValue,
    string? NewValue,
    string Reason,
    Guid? ChangedBy,
    string? ChangedByName,
    DateTimeOffset ChangedAt);

public sealed record AccountIdentitySummary(string Provider, string ProviderUserId, string Username);

/// <summary>An account as the admin accounts page lists it.</summary>
public sealed record AccountAccessRow(
    Guid Id,
    string Username,
    string? GlobalName,
    string? AvatarUrl,
    UserRole Role,
    AccountTier? GrantedTier,
    List<AccountIdentitySummary> Identities);

public interface IAccountAccessStore
{
    Task<AccountTier?> GetGrantedTier(Guid userId);

    /// <summary>Accounts granted <paramref name="minimum"/> or a better tier.</summary>
    Task<List<Guid>> GetUserIdsWithGrantedTierAtLeast(AccountTier minimum);

    /// <summary>Accounts whose username or display name contains the query, or with an identity of that exact id.</summary>
    Task<List<AccountAccessRow>> FindAccounts(string query, int limit);

    Task<AccountAccessRow?> GetAccount(Guid userId);

    /// <summary>The account's recorded changes, newest first.</summary>
    Task<List<AccountAccessChange>> GetChanges(Guid userId);

    /// <summary>Sets the role and records the change in one transaction; refuses to demote the last Admin.</summary>
    Task<AccessChangeOutcome> SetRole(Guid userId, UserRole role, Guid changedBy, string reason);

    /// <summary>Sets or clears (null) the granted tier and records the change in one transaction.</summary>
    Task<AccessChangeOutcome> SetGrantedTier(Guid userId, AccountTier? tier, Guid changedBy, string reason);
}

/// <summary>
/// The only SQL that writes <c>app_user.role</c> and <c>app_user.granted_tier</c>. Accounts being deleted are treated as
/// gone, as everywhere else.
/// </summary>
public sealed class AccountAccessStatements(NpgsqlConnection connection) : IAccountAccessStore
{
    private const string RoleLockKey = "account-access:roles";

    private const string AccountColumns = "id, username, global_name, avatar_url, role, granted_tier";

    public async Task<AccountTier?> GetGrantedTier(Guid userId)
    {
        string? tier = await connection.QuerySingleOrDefaultAsync<string?>(
            "SELECT granted_tier FROM app_user WHERE id = @userId", new { userId });
        return GrantedTiers.Parse(tier);
    }

    public async Task<List<Guid>> GetUserIdsWithGrantedTierAtLeast(AccountTier minimum)
    {
        string[] tiers = GrantedTiers.All.Where(tier => tier >= minimum).Select(GrantedTiers.Name).ToArray();
        const string sql = """
            SELECT id FROM app_user
            WHERE granted_tier = ANY(@tiers) AND deletion_requested_at IS NULL
            """;
        return (await connection.QueryAsync<Guid>(sql, new { tiers })).ToList();
    }

    public async Task<List<AccountAccessRow>> FindAccounts(string query, int limit)
    {
        string pattern = "%" + EscapeLike(query) + "%";
        string sql = $"""
            SELECT {AccountColumns}
            FROM app_user
            WHERE deletion_requested_at IS NULL
              AND (username ILIKE @pattern
                   OR global_name ILIKE @pattern
                   OR EXISTS (SELECT 1 FROM user_identity identity
                              WHERE identity.user_id = app_user.id AND identity.provider_user_id = @query))
            ORDER BY (role = 'Admin') DESC, (granted_tier IS NOT NULL) DESC, username, id
            LIMIT @limit
            """;
        List<AccountRow> rows =
            (await connection.QueryAsync<AccountRow>(sql, new { pattern, query, limit })).ToList();
        return await WithIdentities(rows);
    }

    public async Task<AccountAccessRow?> GetAccount(Guid userId)
    {
        string sql = $"""
            SELECT {AccountColumns}
            FROM app_user
            WHERE id = @userId AND deletion_requested_at IS NULL
            """;
        AccountRow? row = await connection.QuerySingleOrDefaultAsync<AccountRow>(sql, new { userId });
        return row is null ? null : (await WithIdentities([row]))[0];
    }

    public async Task<List<AccountAccessChange>> GetChanges(Guid userId)
    {
        const string sql = """
            SELECT change.id, change.field, change.old_value, change.new_value, change.reason, change.changed_by,
                   COALESCE(admin.global_name, admin.username) AS changed_by_name, change.changed_at
            FROM account_access_change change
            LEFT JOIN app_user admin ON admin.id = change.changed_by
            WHERE change.user_id = @userId
            ORDER BY change.changed_at DESC, change.id
            """;
        return (await connection.QueryAsync<ChangeRow>(sql, new { userId }))
            .Select(row => new AccountAccessChange(row.Id, row.Field, row.OldValue, row.NewValue, row.Reason,
                row.ChangedBy, row.ChangedByName, row.ChangedAt))
            .ToList();
    }

    public async Task<AccessChangeOutcome> SetRole(Guid userId, UserRole role, Guid changedBy, string reason)
    {
        await OpenAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();

        // Role changes are serialised so two admins demoting each other can't both pass the last-Admin check.
        await connection.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtext(@key))", new { key = RoleLockKey },
            transaction);

        string? current = await connection.QuerySingleOrDefaultAsync<string?>(
            "SELECT role FROM app_user WHERE id = @userId AND deletion_requested_at IS NULL FOR UPDATE",
            new { userId }, transaction);
        if (current is null)
        {
            return AccessChangeOutcome.NotFound;
        }

        string newRole = role.ToString();
        if (current == newRole)
        {
            return AccessChangeOutcome.Unchanged;
        }

        if (current == nameof(UserRole.Admin))
        {
            int otherAdmins = await connection.QuerySingleAsync<int>(
                """
                SELECT count(*)::int FROM app_user
                WHERE role = 'Admin' AND id <> @userId AND deletion_requested_at IS NULL
                """,
                new { userId }, transaction);
            if (otherAdmins == 0)
            {
                return AccessChangeOutcome.LastAdmin;
            }
        }

        await connection.ExecuteAsync("UPDATE app_user SET role = @newRole WHERE id = @userId",
            new { userId, newRole }, transaction);
        await RecordChange(userId, AccessFields.Role, current, newRole, changedBy, reason, transaction);
        await transaction.CommitAsync();
        return AccessChangeOutcome.Changed;
    }

    public async Task<AccessChangeOutcome> SetGrantedTier(Guid userId, AccountTier? tier, Guid changedBy,
        string reason)
    {
        await OpenAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();

        CurrentTierRow? current = await connection.QuerySingleOrDefaultAsync<CurrentTierRow>(
            "SELECT granted_tier FROM app_user WHERE id = @userId AND deletion_requested_at IS NULL FOR UPDATE",
            new { userId }, transaction);
        if (current is null)
        {
            return AccessChangeOutcome.NotFound;
        }

        string? newTier = tier is { } value ? GrantedTiers.Name(value) : null;
        if (current.GrantedTier == newTier)
        {
            return AccessChangeOutcome.Unchanged;
        }

        await connection.ExecuteAsync("UPDATE app_user SET granted_tier = @newTier WHERE id = @userId",
            new { userId, newTier }, transaction);
        await RecordChange(userId, AccessFields.GrantedTier, current.GrantedTier, newTier, changedBy, reason,
            transaction);
        await transaction.CommitAsync();
        return AccessChangeOutcome.Changed;
    }

    private async Task RecordChange(Guid userId, string field, string? oldValue, string? newValue, Guid changedBy,
        string reason, NpgsqlTransaction transaction)
    {
        const string sql = """
            INSERT INTO account_access_change (user_id, changed_by, field, old_value, new_value, reason)
            VALUES (@userId, @changedBy, @field, @oldValue, @newValue, @reason)
            """;
        await connection.ExecuteAsync(sql, new { userId, changedBy, field, oldValue, newValue, reason },
            transaction);
    }

    private async Task<List<AccountAccessRow>> WithIdentities(List<AccountRow> rows)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        Guid[] ids = rows.Select(row => row.Id).ToArray();
        const string sql = """
            SELECT user_id, provider, provider_user_id, username
            FROM user_identity
            WHERE user_id = ANY(@ids)
            ORDER BY linked_at, id
            """;
        ILookup<Guid, IdentityRow> identities =
            (await connection.QueryAsync<IdentityRow>(sql, new { ids })).ToLookup(identity => identity.UserId);
        return rows.Select(row => new AccountAccessRow(row.Id, row.Username, row.GlobalName, row.AvatarUrl,
                UserRoles.Parse(row.Role), GrantedTiers.Parse(row.GrantedTier),
                identities[row.Id]
                    .Select(identity =>
                        new AccountIdentitySummary(identity.Provider, identity.ProviderUserId, identity.Username))
                    .ToList()))
            .ToList();
    }

    private async Task OpenAsync()
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }
    }

    private static string EscapeLike(string value)
    {
        return value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
    }

    private sealed class AccountRow
    {
        public Guid Id { get; set; }
        public string Username { get; set; } = "";
        public string? GlobalName { get; set; }
        public string? AvatarUrl { get; set; }
        public string Role { get; set; } = "";
        public string? GrantedTier { get; set; }
    }

    private sealed class ChangeRow
    {
        public Guid Id { get; set; }
        public string Field { get; set; } = "";
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
        public string Reason { get; set; } = "";
        public Guid? ChangedBy { get; set; }
        public string? ChangedByName { get; set; }
        public DateTimeOffset ChangedAt { get; set; }
    }

    private sealed class CurrentTierRow
    {
        public string? GrantedTier { get; set; }
    }

    private sealed class IdentityRow
    {
        public Guid UserId { get; set; }
        public string Provider { get; set; } = "";
        public string ProviderUserId { get; set; } = "";
        public string Username { get; set; } = "";
    }
}
