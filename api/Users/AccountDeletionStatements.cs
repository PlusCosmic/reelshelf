using Dapper;
using Npgsql;
using Reelshelf.Moderation;
using Reelshelf.Video;

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
        const string sql = "SELECT id AS clip_id, video_id, video_host FROM clip WHERE owner_id = @userId";
        return (await connection.QueryAsync<ClipVideoRow>(sql, new { userId }))
            .Select(row => new OwnedClipVideo(row.ClipId, row.VideoId, row.VideoHost))
            .ToList();
    }

    /// <summary>A clip of the account's, removed after an urgent report, whose video isn't in the evidence bucket yet.</summary>
    private const string UncopiedEvidenceSql =
        "EXISTS (SELECT 1 FROM clip_evidence e WHERE e.owner_id = @userId AND e.files IS NULL)";

    private static readonly string OpenUrgentReportSql = $"""
        EXISTS (
            SELECT 1 FROM content_report r
            WHERE r.clip_id = c.id AND r.status = 'open' AND r.reason IN ({ContentReportReasons.UrgentSqlList}))
        """;

    public async Task<bool> IsHeldForReview(Guid userId)
    {
        string sql = $"""
            SELECT EXISTS (SELECT 1 FROM clip c WHERE c.owner_id = @userId AND {OpenUrgentReportSql})
                OR {UncopiedEvidenceSql}
            """;
        return await connection.QuerySingleAsync<bool>(sql, new { userId });
    }

    public async Task DeleteClips(Guid userId, IReadOnlyCollection<Guid> clipIds)
    {
        string sql = $"""
            DELETE FROM clip c
            WHERE c.owner_id = @userId AND c.id = ANY(@clipIds) AND NOT {OpenUrgentReportSql}
            """;
        await connection.ExecuteAsync(sql, new { userId, clipIds = clipIds.ToArray() });
    }

    public async Task<bool> DeleteAccountIfEmpty(Guid userId)
    {
        const string sql = $"""
            DELETE FROM app_user
            WHERE id = @userId
              AND deletion_requested_at IS NOT NULL
              AND NOT EXISTS (SELECT 1 FROM clip WHERE owner_id = @userId)
              AND NOT {UncopiedEvidenceSql}
            """;
        return await connection.ExecuteAsync(sql, new { userId }) > 0;
    }

    private sealed class ClipVideoRow
    {
        public Guid ClipId { get; set; }
        public Guid VideoId { get; set; }
        public string VideoHost { get; set; } = "";
    }
}
