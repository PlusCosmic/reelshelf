using Dapper;
using Npgsql;

namespace Nucleus.Clips.Playlists;

public class PlaylistStatements(NpgsqlConnection connection)
{
    public async Task<PlaylistRow> InsertPlaylist(string name, string? description, Guid creatorUserId)
    {
        const string sql = @"
            INSERT INTO playlists (name, description, creator_user_id, created_at, updated_at)
            VALUES (@name, @description, @creatorUserId, NOW(), NOW())
            RETURNING id, name, description, creator_user_id, created_at, updated_at";

        return await connection.QuerySingleAsync<PlaylistRow>(sql, new { name, description, creatorUserId });
    }

    public async Task AddCollaborator(Guid playlistId, Guid userId, Guid addedByUserId)
    {
        const string sql = @"
            INSERT INTO playlist_collaborators (playlist_id, user_id, added_by_user_id, added_at)
            VALUES (@playlistId, @userId, @addedByUserId, NOW())
            ON CONFLICT (playlist_id, user_id) DO NOTHING";

        await connection.ExecuteAsync(sql, new { playlistId, userId, addedByUserId });
    }

    public async Task RemoveCollaborator(Guid playlistId, Guid userId)
    {
        const string sql = @"
            DELETE FROM playlist_collaborators
            WHERE playlist_id = @playlistId AND user_id = @userId";

        await connection.ExecuteAsync(sql, new { playlistId, userId });
    }

    public async Task<int> GetCollaboratorCount(Guid playlistId)
    {
        const string sql = @"
            SELECT COUNT(*)
            FROM playlist_collaborators
            WHERE playlist_id = @playlistId";

        return await connection.ExecuteScalarAsync<int>(sql, new { playlistId });
    }

    public async Task<List<PlaylistSummaryRow>> GetPlaylistsByUserId(Guid userId)
    {
        const string sql = @"
            SELECT
                p.id,
                p.name,
                p.description,
                p.creator_user_id,
                p.created_at,
                p.updated_at,
                COUNT(DISTINCT pc.clip_id) as clip_count,
                COUNT(DISTINCT pcollab.user_id) as collaborator_count
            FROM playlists p
            INNER JOIN playlist_collaborators pcollab ON p.id = pcollab.playlist_id
            LEFT JOIN playlist_clips pc ON p.id = pc.playlist_id
            WHERE pcollab.user_id = @userId
            GROUP BY p.id, p.name, p.description, p.creator_user_id, p.created_at, p.updated_at
            ORDER BY p.updated_at DESC";

        var results = await connection.QueryAsync<PlaylistSummaryRow>(sql, new { userId });
        return results.ToList();
    }

    public async Task<PlaylistRow?> GetPlaylistById(Guid playlistId)
    {
        const string sql = @"
            SELECT id, name, description, creator_user_id, created_at, updated_at
            FROM playlists
            WHERE id = @playlistId";

        return await connection.QuerySingleOrDefaultAsync<PlaylistRow>(sql, new { playlistId });
    }

    public async Task<List<PlaylistCollaboratorRow>> GetPlaylistCollaborators(Guid playlistId)
    {
        const string sql = @"
            SELECT
                pc.user_id,
                pc.added_at,
                pc.added_by_user_id,
                du.username,
                du.avatar as avatar_url
            FROM playlist_collaborators pc
            INNER JOIN discord_user du ON pc.user_id = du.id
            WHERE pc.playlist_id = @playlistId
            ORDER BY pc.added_at ASC";

        var results = await connection.QueryAsync<PlaylistCollaboratorRow>(sql, new { playlistId });
        return results.ToList();
    }

    public async Task<List<PlaylistClipRow>> GetPlaylistClips(Guid playlistId)
    {
        const string sql = @"
            SELECT
                pc.id,
                pc.playlist_id,
                pc.clip_id,
                pc.position,
                pc.added_by_user_id,
                pc.added_at
            FROM playlist_clips pc
            WHERE pc.playlist_id = @playlistId
            ORDER BY pc.position ASC";

        var results = await connection.QueryAsync<PlaylistClipRow>(sql, new { playlistId });
        return results.ToList();
    }

    public async Task<bool> IsUserCollaborator(Guid playlistId, Guid userId)
    {
        const string sql = @"
            SELECT EXISTS(
                SELECT 1
                FROM playlist_collaborators
                WHERE playlist_id = @playlistId AND user_id = @userId
            )";

        return await connection.ExecuteScalarAsync<bool>(sql, new { playlistId, userId });
    }

    public async Task UpdatePlaylist(Guid playlistId, string? name, string? description)
    {
        List<string> updates = [];
        var parameters = new DynamicParameters();
        parameters.Add("playlistId", playlistId);

        if (name != null)
        {
            updates.Add("name = @name");
            parameters.Add("name", name);
        }

        if (description != null)
        {
            updates.Add("description = @description");
            parameters.Add("description", description);
        }

        if (updates.Count == 0)
        {
            return; // Nothing to update
        }

        updates.Add("updated_at = NOW()");

        string sql = $@"
            UPDATE playlists
            SET {string.Join(", ", updates)}
            WHERE id = @playlistId";

        await connection.ExecuteAsync(sql, parameters);
    }

    public async Task DeletePlaylist(Guid playlistId)
    {
        const string sql = "DELETE FROM playlists WHERE id = @playlistId";
        await connection.ExecuteAsync(sql, new { playlistId });
    }

    // Playlist Clips Methods

    public async Task<int> GetMaxPosition(Guid playlistId)
    {
        const string sql = @"
            SELECT COALESCE(MAX(position), -1)
            FROM playlist_clips
            WHERE playlist_id = @playlistId";

        return await connection.ExecuteScalarAsync<int>(sql, new { playlistId });
    }

    public async Task<bool> ClipExistsInPlaylist(Guid playlistId, Guid clipId)
    {
        const string sql = @"
            SELECT EXISTS(
                SELECT 1
                FROM playlist_clips
                WHERE playlist_id = @playlistId AND clip_id = @clipId
            )";

        return await connection.ExecuteScalarAsync<bool>(sql, new { playlistId, clipId });
    }

    public async Task<PlaylistClipRow> AddClipToPlaylist(Guid playlistId, Guid clipId, Guid addedByUserId, int position)
    {
        const string sql = @"
            INSERT INTO playlist_clips (playlist_id, clip_id, position, added_by_user_id, added_at)
            VALUES (@playlistId, @clipId, @position, @addedByUserId, NOW())
            RETURNING id, playlist_id, clip_id, position, added_by_user_id, added_at";

        return await connection.QuerySingleAsync<PlaylistClipRow>(sql, new { playlistId, clipId, position, addedByUserId });
    }

    public async Task RemoveClipFromPlaylist(Guid playlistId, Guid clipId)
    {
        const string sql = @"
            DELETE FROM playlist_clips
            WHERE playlist_id = @playlistId AND clip_id = @clipId";

        await connection.ExecuteAsync(sql, new { playlistId, clipId });
    }

    public async Task UpdateClipPosition(Guid playlistClipId, int newPosition)
    {
        const string sql = @"
            UPDATE playlist_clips
            SET position = @newPosition
            WHERE id = @playlistClipId";

        await connection.ExecuteAsync(sql, new { playlistClipId, newPosition });
    }

    public async Task ReorderClips(Guid playlistId, List<Guid> clipOrdering)
    {
        for (int i = 0; i < clipOrdering.Count; i++)
        {
            const string sql = @"
                UPDATE playlist_clips
                SET position = @position
                WHERE playlist_id = @playlistId AND clip_id = @clipId";

            await connection.ExecuteAsync(sql, new { playlistId, clipId = clipOrdering[i], position = i });
        }
    }

    public async Task TouchPlaylistUpdatedAt(Guid playlistId)
    {
        const string sql = @"
            UPDATE playlists
            SET updated_at = NOW()
            WHERE id = @playlistId";

        await connection.ExecuteAsync(sql, new { playlistId });
    }

    // Database Models (PascalCase properties auto-mapped to snake_case via DefaultTypeMap.MatchNamesWithUnderscores)
    public class PlaylistRow
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public Guid CreatorUserId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }

    public class PlaylistSummaryRow
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public Guid CreatorUserId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public int ClipCount { get; set; }
        public int CollaboratorCount { get; set; }
    }

    public class PlaylistCollaboratorRow
    {
        public Guid UserId { get; set; }
        public DateTimeOffset AddedAt { get; set; }
        public Guid AddedByUserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
    }

    public class PlaylistClipRow
    {
        public Guid Id { get; set; }
        public Guid PlaylistId { get; set; }
        public Guid ClipId { get; set; }
        public int Position { get; set; }
        public Guid AddedByUserId { get; set; }
        public DateTimeOffset AddedAt { get; set; }
    }
}
