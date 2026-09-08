using Dapper;
using Npgsql;

namespace Reelshelf.Discord;

public class DiscordStatements(NpgsqlConnection connection)
{
    public async Task<DiscordUserRow?> GetUserByDiscordId(string discordId)
    {
        const string sql = @"
            SELECT id, discord_id, username, global_name, avatar, role, role_from_whitelist
            FROM discord_user
            WHERE discord_id = @discordId
            LIMIT 1";

        return await connection.QuerySingleOrDefaultAsync<DiscordUserRow>(sql, new { discordId });
    }

    public async Task<DiscordUserRow?> GetUserById(Guid id)
    {
        const string sql = @"
            SELECT id, discord_id, username, global_name, avatar, role, role_from_whitelist
            FROM discord_user
            WHERE id = @id
            LIMIT 1";

        return await connection.QuerySingleOrDefaultAsync<DiscordUserRow>(sql, new { id });
    }

    public async Task<DiscordUserRow?> GetUserByUsername(string username)
    {
        const string sql = @"
            SELECT id, discord_id, username, global_name, avatar, role, role_from_whitelist
            FROM discord_user
            WHERE username = @username
            LIMIT 1";

        return await connection.QuerySingleOrDefaultAsync<DiscordUserRow>(sql, new { username });
    }

    public async Task<DiscordUserRow> InsertUser(string discordId, string username, string? globalName, string? avatar)
    {
        const string sql = @"
            INSERT INTO discord_user (discord_id, username, global_name, avatar)
            VALUES (@discordId, @username, @globalName, @avatar)
            RETURNING id, discord_id, username, global_name, avatar, role, role_from_whitelist";

        return await connection.QuerySingleAsync<DiscordUserRow>(sql, new { discordId, username, globalName, avatar });
    }

    public async Task UpdateUser(Guid id, string username, string? globalName, string? avatar)
    {
        const string sql = @"
            UPDATE discord_user
            SET username = @username, global_name = @globalName, avatar = @avatar
            WHERE id = @id";

        await connection.ExecuteAsync(sql, new { id, username, globalName, avatar });
    }

    public async Task<DiscordUserRow> UpsertUser(string discordId, string username, string? globalName, string? avatar)
    {
        const string sql = @"
            INSERT INTO discord_user (discord_id, username, global_name, avatar)
            VALUES (@discordId, @username, @globalName, @avatar)
            ON CONFLICT (discord_id)
            DO UPDATE SET
                username = EXCLUDED.username,
                global_name = EXCLUDED.global_name,
                avatar = EXCLUDED.avatar
            RETURNING id, discord_id, username, global_name, avatar, role, role_from_whitelist";

        return await connection.QuerySingleAsync<DiscordUserRow>(sql, new { discordId, username, globalName, avatar });
    }

    /// <summary>
    /// Users who have added <paramref name="userId"/> as a collaborator on a playlist they created.
    /// Being added is a deliberate act by that user, so this is the set of people who have opted in to
    /// sharing with the caller; it cannot be manufactured by the caller.
    /// </summary>
    public async Task<List<DiscordUserRow>> GetUsersWhoAddedMe(Guid userId)
    {
        const string sql = @"
            SELECT DISTINCT u.id, u.discord_id, u.username, u.global_name, u.avatar, u.role, u.role_from_whitelist
            FROM playlist_collaborators pc
            JOIN playlists p ON p.id = pc.playlist_id
            JOIN discord_user u ON u.id = p.creator_user_id
            WHERE pc.user_id = @userId
              AND p.creator_user_id != @userId";

        var result = await connection.QueryAsync<DiscordUserRow>(sql, new { userId });
        return result.ToList();
    }

    /// <summary>
    /// Users who already share a playlist with <paramref name="userId"/>: collaborators on playlists they own,
    /// and owners plus fellow collaborators of playlists they collaborate on. Never lists strangers.
    /// </summary>
    public async Task<List<DiscordUserRow>> GetPlaylistPeers(Guid userId)
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
            SELECT u.id, u.discord_id, u.username, u.global_name, u.avatar, u.role
            FROM discord_user u
            JOIN peer_ids pi ON pi.user_id = u.id
            WHERE u.id != @userId
            ORDER BY u.global_name, u.username";

        var result = await connection.QueryAsync<DiscordUserRow>(sql, new { userId });
        return result.ToList();
    }

    public async Task<List<DiscordUserRow>> GetAllUsers()
    {
        const string sql = @"
            SELECT id, discord_id, username, global_name, avatar, role, role_from_whitelist
            FROM discord_user
            ORDER BY role DESC, global_name, username";

        var result = await connection.QueryAsync<DiscordUserRow>(sql);
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
            UPDATE discord_user
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

    public class DiscordUserRow
    {
        public Guid Id { get; set; }
        public string DiscordId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string? GlobalName { get; set; }
        public string? Avatar { get; set; }
        public string Role { get; set; } = "Editor";
        public bool RoleFromWhitelist { get; set; }
    }
}
