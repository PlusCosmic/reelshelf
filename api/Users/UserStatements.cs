using Dapper;
using Npgsql;

namespace Reelshelf.Users;

public class UserStatements(NpgsqlConnection connection) : IUserIdentityStore, Storage.IStorageWarningStore
{
    private const string UserColumns = "id, username, global_name, avatar_url, email, onboarding_completed_at, role, role_from_whitelist";

    private const string IdentityColumns =
        "id, user_id, provider, provider_user_id, username, display_name, avatar_url, email, linked_at";

    public async Task<UserRow?> GetUserById(Guid id)
    {
        string sql = $@"
            SELECT {UserColumns}
            FROM app_user
            WHERE id = @id
            LIMIT 1";

        return await connection.QuerySingleOrDefaultAsync<UserRow>(sql, new { id });
    }

    /// <summary>
    /// Accounts whose username matches exactly. Usernames come from different providers, so more than
    /// one account can legitimately share one; callers decide what to do with an ambiguous match.
    /// </summary>
    public async Task<List<UserRow>> GetUsersByUsername(string username)
    {
        string sql = $@"
            SELECT {UserColumns}
            FROM app_user
            WHERE username = @username
            ORDER BY id
            LIMIT 2";

        IEnumerable<UserRow> result = await connection.QueryAsync<UserRow>(sql, new { username });
        return result.ToList();
    }

    public async Task<UserIdentityRow?> GetIdentity(string provider, string providerUserId)
    {
        string sql = $@"
            SELECT {IdentityColumns}
            FROM user_identity
            WHERE provider = @provider AND provider_user_id = @providerUserId
            LIMIT 1";

        return await connection.QuerySingleOrDefaultAsync<UserIdentityRow>(sql, new { provider, providerUserId });
    }

    /// <summary>Identities linked to an account, oldest first; the first one is the primary identity.</summary>
    public async Task<List<UserIdentityRow>> GetIdentitiesForUser(Guid userId)
    {
        string sql = $@"
            SELECT {IdentityColumns}
            FROM user_identity
            WHERE user_id = @userId
            ORDER BY linked_at, id";

        IEnumerable<UserIdentityRow> result = await connection.QueryAsync<UserIdentityRow>(sql, new { userId });
        return result.ToList();
    }

    /// <summary>
    /// Creates an account owning <paramref name="identity"/>. Returns null when the identity already exists,
    /// i.e. a concurrent first sign-in won; the account row inserted here is rolled back with the transaction.
    /// </summary>
    public async Task<UserRow?> CreateUserWithIdentity(ExternalIdentity identity)
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();

        string insertUser = $@"
            INSERT INTO app_user (username, global_name, avatar_url)
            VALUES (@username, @globalName, @avatarUrl)
            RETURNING {UserColumns}";

        UserRow user = await connection.QuerySingleAsync<UserRow>(
            insertUser,
            new { username = identity.Username, globalName = identity.DisplayName, avatarUrl = identity.AvatarUrl },
            transaction);

        try
        {
            await InsertIdentity(user.Id, identity, transaction);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync();
            return null;
        }

        await transaction.CommitAsync();
        return user;
    }

    /// <summary>Returns null when the identity already exists for some account (unique violation).</summary>
    public async Task<UserIdentityRow?> LinkIdentity(Guid userId, ExternalIdentity identity)
    {
        try
        {
            return await InsertIdentity(userId, identity, transaction: null);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return null;
        }
    }

    /// <summary>
    /// Opens a transaction holding a per-account advisory lock so identity checks and the writes that follow
    /// cannot interleave with another link or unlink for the same account. Dispose without committing to roll back.
    /// </summary>
    public async Task<IAccountScope> LockAccount(Guid userId)
    {
        bool openedConnection = false;
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
            openedConnection = true;
        }

        NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
        await connection.ExecuteAsync(
            "SELECT pg_advisory_xact_lock(hashtext(@key))",
            new { key = $"account:{userId}" },
            transaction);
        return new AccountScope(connection, transaction, openedConnection);
    }

    private sealed class AccountScope(NpgsqlConnection connection, NpgsqlTransaction transaction, bool openedConnection)
        : IAccountScope
    {
        public Task CommitAsync() => transaction.CommitAsync();

        public async ValueTask DisposeAsync()
        {
            await transaction.DisposeAsync();
            if (openedConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task<UserIdentityRow> InsertIdentity(Guid userId, ExternalIdentity identity, NpgsqlTransaction? transaction)
    {
        string sql = $@"
            INSERT INTO user_identity (user_id, provider, provider_user_id, username, display_name, avatar_url, email)
            VALUES (@userId, @provider, @providerUserId, @username, @displayName, @avatarUrl, @email)
            RETURNING {IdentityColumns}";

        return await connection.QuerySingleAsync<UserIdentityRow>(
            sql,
            new
            {
                userId,
                provider = identity.Provider,
                providerUserId = identity.ProviderUserId,
                username = identity.Username,
                displayName = identity.DisplayName,
                avatarUrl = identity.AvatarUrl,
                email = identity.Email
            },
            transaction);
    }

    public async Task UpdateIdentityProfile(Guid identityId, ExternalIdentity identity)
    {
        const string sql = @"
            UPDATE user_identity
            SET username = @username,
                display_name = @displayName,
                avatar_url = @avatarUrl,
                email = @email
            WHERE id = @identityId";

        await connection.ExecuteAsync(sql, new
        {
            identityId,
            username = identity.Username,
            displayName = identity.DisplayName,
            avatarUrl = identity.AvatarUrl,
            email = identity.Email
        });
    }

    public async Task UpdateUserProfile(Guid userId, string username, string? globalName, string? avatarUrl)
    {
        const string sql = @"
            UPDATE app_user
            SET username = @username,
                global_name = @globalName,
                avatar_url = @avatarUrl
            WHERE id = @userId";

        await connection.ExecuteAsync(sql, new { userId, username, globalName, avatarUrl });
    }

    public async Task<Storage.StorageWarningState> GetState(Guid userId)
    {
        const string sql = "SELECT email, storage_warned_at AS warned_at FROM app_user WHERE id = @userId";
        return await connection.QuerySingleOrDefaultAsync<Storage.StorageWarningState>(sql, new { userId })
               ?? new Storage.StorageWarningState(null, null);
    }

    // Same usage expression as ClipsStatements.GetStorageUsedBytesByOwner, evaluated in the update itself.
    private const string LiveUsageSql =
        "(SELECT COALESCE(SUM(GREATEST(COALESCE(file_size, 0), COALESCE(storage_size, 0))), 0) FROM clip WHERE owner_id = @userId)";

    public async Task<bool> MarkWarned(Guid userId, long thresholdBytes)
    {
        string sql = $@"
            UPDATE app_user
            SET storage_warned_at = now()
            WHERE id = @userId
              AND storage_warned_at IS NULL
              AND {LiveUsageSql} >= @thresholdBytes";
        return await connection.ExecuteAsync(sql, new { userId, thresholdBytes }) == 1;
    }

    public async Task ClearWarned(Guid userId, long thresholdBytes)
    {
        string sql = $@"
            UPDATE app_user
            SET storage_warned_at = NULL
            WHERE id = @userId
              AND storage_warned_at IS NOT NULL
              AND {LiveUsageSql} < @thresholdBytes";
        await connection.ExecuteAsync(sql, new { userId, thresholdBytes });
    }

    /// <summary>Records the address the user chose for account mail (null when they skipped) and marks onboarding done.</summary>
    public async Task SetUserEmail(Guid userId, string? email)
    {
        const string sql = @"
            UPDATE app_user
            SET email = @email,
                onboarding_completed_at = COALESCE(onboarding_completed_at, now())
            WHERE id = @userId";

        await connection.ExecuteAsync(sql, new { userId, email });
    }

    public async Task DeleteIdentity(Guid identityId)
    {
        const string sql = "DELETE FROM user_identity WHERE id = @identityId";
        await connection.ExecuteAsync(sql, new { identityId });
    }

    /// <summary>
    /// Users who have personally added <paramref name="userId"/> as a collaborator on a playlist they created.
    /// Being added by the creator is a deliberate act by that user, so this is the set of people who have
    /// opted in to sharing with the caller. Rows added by other collaborators do not count, because any
    /// collaborator can add users and that would let a third party manufacture the creator's consent.
    /// </summary>
    public async Task<List<UserRow>> GetUsersWhoAddedMe(Guid userId)
    {
        const string sql = @"
            SELECT DISTINCT u.id, u.username, u.global_name, u.avatar_url, u.email, u.onboarding_completed_at, u.role, u.role_from_whitelist
            FROM playlist_collaborators pc
            JOIN playlists p ON p.id = pc.playlist_id
            JOIN app_user u ON u.id = p.creator_user_id
            WHERE pc.user_id = @userId
              AND pc.added_by_user_id = p.creator_user_id
              AND p.creator_user_id != @userId";

        var result = await connection.QueryAsync<UserRow>(sql, new { userId });
        return result.ToList();
    }

    /// <summary>
    /// Users who already share a playlist with <paramref name="userId"/>: collaborators on playlists they own,
    /// and owners plus fellow collaborators of playlists they collaborate on. Never lists strangers.
    /// </summary>
    public async Task<List<UserRow>> GetPlaylistPeers(Guid userId)
    {
        const string sql = @"
            WITH my_playlists AS (
                SELECT id FROM playlists WHERE creator_user_id = @userId
                UNION
                SELECT playlist_id FROM playlist_collaborators WHERE user_id = @userId
            ),
            peer_ids AS (
                SELECT p.creator_user_id AS user_id
                FROM playlists p
                JOIN my_playlists mp ON mp.id = p.id
                UNION
                SELECT pc.user_id
                FROM playlist_collaborators pc
                JOIN my_playlists mp ON mp.id = pc.playlist_id
            )
            SELECT u.id, u.username, u.global_name, u.avatar_url, u.role
            FROM app_user u
            JOIN peer_ids pi ON pi.user_id = u.id
            WHERE u.id != @userId
            ORDER BY u.global_name, u.username";

        var result = await connection.QueryAsync<UserRow>(sql, new { userId });
        return result.ToList();
    }

    public async Task<List<UserRow>> GetAllUsers()
    {
        string sql = $@"
            SELECT {UserColumns}
            FROM app_user
            ORDER BY role DESC, global_name, username";

        var result = await connection.QueryAsync<UserRow>(sql);
        return result.ToList();
    }

    public async Task<List<string>> GetUserAdditionalPermissions(Guid userId)
    {
        const string sql = @"
            SELECT permission
            FROM user_additional_permission
            WHERE user_id = @userId";

        IEnumerable<string> permissions = await connection.QueryAsync<string>(sql, new { userId });
        return permissions.ToList();
    }

    public async Task UpdateUserRole(Guid userId, string role, bool roleFromWhitelist)
    {
        const string sql = @"
            UPDATE app_user
            SET role = @role,
                role_from_whitelist = @roleFromWhitelist
            WHERE id = @userId";

        await connection.ExecuteAsync(sql, new { userId, role, roleFromWhitelist });
    }

    public async Task GrantPermission(Guid userId, string permission, Guid? grantedBy = null)
    {
        const string sql = @"
            INSERT INTO user_additional_permission (user_id, permission, granted_by)
            VALUES (@userId, @permission, @grantedBy)
            ON CONFLICT (user_id, permission) DO NOTHING";

        await connection.ExecuteAsync(sql, new { userId, permission, grantedBy });
    }

    public async Task RevokePermission(Guid userId, string permission)
    {
        const string sql = @"
            DELETE FROM user_additional_permission
            WHERE user_id = @userId AND permission = @permission";

        await connection.ExecuteAsync(sql, new { userId, permission });
    }

    public class UserRow
    {
        public Guid Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string? GlobalName { get; set; }
        public string? AvatarUrl { get; set; }
        public string? Email { get; set; }
        public DateTimeOffset? OnboardingCompletedAt { get; set; }
        public string Role { get; set; } = "Editor";
        public bool RoleFromWhitelist { get; set; }
    }

    public class UserIdentityRow
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string Provider { get; set; } = string.Empty;
        public string ProviderUserId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string? AvatarUrl { get; set; }
        public string? Email { get; set; }
        public DateTimeOffset LinkedAt { get; set; }

        public UserIdentityRef ToRef() => new(Provider, ProviderUserId);
    }
}
