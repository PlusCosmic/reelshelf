using Dapper;
using Npgsql;

namespace Reelshelf.Users;

public class AccountDeletionStatements(NpgsqlConnection connection) : IAccountDeletionStore
{
    public async Task<AccountDeletionRequest?> MarkForDeletion(Guid userId)
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();

        // The same lock as identity linking and unlinking, so no identity can be added to the account mid-way.
        await connection.ExecuteAsync(
            "SELECT pg_advisory_xact_lock(hashtext(@key))",
            new { key = $"account:{userId}" },
            transaction);

        const string mark = """
            UPDATE app_user
            SET deletion_requested_at = now()
            WHERE id = @userId AND deletion_requested_at IS NULL
            RETURNING email
            """;
        IEnumerable<string?> marked = await connection.QueryAsync<string?>(mark, new { userId }, transaction);
        List<string?> emails = marked.ToList();
        if (emails.Count == 0)
        {
            return null;
        }

        // Removing the identities stops them signing in to this account and drops the stored Twitch tokens. Signing
        // in with them again creates a new, empty account.
        await connection.ExecuteAsync("DELETE FROM user_identity WHERE user_id = @userId", new { userId }, transaction);

        await transaction.CommitAsync();
        return new AccountDeletionRequest(emails[0]);
    }

    public async Task<List<Guid>> GetAccountsPendingDeletion()
    {
        const string sql = """
            SELECT id
            FROM app_user
            WHERE deletion_requested_at IS NOT NULL
            ORDER BY deletion_requested_at
            """;
        return (await connection.QueryAsync<Guid>(sql)).ToList();
    }

    public async Task<List<OwnedClipVideo>> GetClipVideos(Guid userId)
    {
        const string sql = "SELECT id AS clip_id, video_id FROM clip WHERE owner_id = @userId";
        return (await connection.QueryAsync<ClipVideoRow>(sql, new { userId }))
            .Select(row => new OwnedClipVideo(row.ClipId, row.VideoId))
            .ToList();
    }

    public async Task DeleteClips(Guid userId, IReadOnlyCollection<Guid> clipIds)
    {
        const string sql = "DELETE FROM clip WHERE owner_id = @userId AND id = ANY(@clipIds)";
        await connection.ExecuteAsync(sql, new { userId, clipIds = clipIds.ToArray() });
    }

    public async Task<List<Guid>> GetBunnyCollections(Guid userId)
    {
        const string sql = "SELECT collection_id FROM clip_collection WHERE owner_id = @userId";
        return (await connection.QueryAsync<Guid>(sql, new { userId })).ToList();
    }

    public async Task<bool> DeleteAccountIfEmpty(Guid userId)
    {
        const string sql = """
            DELETE FROM app_user
            WHERE id = @userId
              AND deletion_requested_at IS NOT NULL
              AND NOT EXISTS (SELECT 1 FROM clip WHERE owner_id = @userId)
            """;
        return await connection.ExecuteAsync(sql, new { userId }) > 0;
    }

    private sealed class ClipVideoRow
    {
        public Guid ClipId { get; set; }
        public Guid VideoId { get; set; }
    }
}
