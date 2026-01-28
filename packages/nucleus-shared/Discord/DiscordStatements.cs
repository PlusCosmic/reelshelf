using Dapper;
using Npgsql;

namespace Nucleus.Shared.Discord;

public class DiscordStatements(NpgsqlConnection connection)
{
    public async Task<DiscordUserRow?> GetUserByDiscordId(string discordId)
    {
        const string sql = @"
            SELECT id, discord_id, username, global_name, avatar, role
            FROM discord_user
            WHERE discord_id = @discordId
            LIMIT 1";

        return await connection.QuerySingleOrDefaultAsync<DiscordUserRow>(sql, new { discordId });
    }

    public async Task<DiscordUserRow?> GetUserById(Guid id)
    {
        const string sql = @"
            SELECT id, discord_id, username, global_name, avatar, role
            FROM discord_user
            WHERE id = @id
            LIMIT 1";

        return await connection.QuerySingleOrDefaultAsync<DiscordUserRow>(sql, new { id });
    }

    public async Task<DiscordUserRow?> GetUserByUsername(string username)
    {
        const string sql = @"
            SELECT id, discord_id, username, global_name, avatar, role
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
            RETURNING id, discord_id, username, global_name, avatar, role";

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
            RETURNING id, discord_id, username, global_name, avatar, role";

        return await connection.QuerySingleAsync<DiscordUserRow>(sql, new { discordId, username, globalName, avatar });
    }

    public async Task<List<DiscordUserRow>> GetAllUsersExcept(string excludeDiscordId)
    {
        const string sql = @"
            SELECT id, discord_id, username, global_name, avatar, role
            FROM discord_user
            WHERE discord_id != @excludeDiscordId
            ORDER BY global_name, username";

        var result = await connection.QueryAsync<DiscordUserRow>(sql, new { excludeDiscordId });
        return result.ToList();
    }

    public async Task<List<DiscordUserRow>> GetAllUsers()
    {
        const string sql = @"
            SELECT id, discord_id, username, global_name, avatar, role
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

    public async Task UpdateUserRole(Guid userId, string role)
    {
        const string sql = @"
            UPDATE discord_user
            SET role = @role
            WHERE id = @userId";

        await connection.ExecuteAsync(sql, new { userId, role });
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
        public string Role { get; set; } = "Viewer";
    }
}
