using Dapper;
using Npgsql;

namespace Reelshelf.Plans;

public sealed record OverLimitState(string? Email, DateTimeOffset? OverLimitSince, DateTimeOffset? WarnedAt);

/// <summary>What the over-limit sweep and the plan page read and write about accounts and their clips.</summary>
public interface IPlanEnforcementStore
{
    /// <summary>
    /// Accounts that may be over their limit: ones already in a grace, and ones with more clips than
    /// <paramref name="smallestLimit"/>. Accounts being deleted are left out.
    /// </summary>
    Task<List<Guid>> GetAccountsToCheck(int smallestLimit);

    Task<OverLimitState> GetState(Guid userId);

    /// <summary>Starts the grace now, unless one is already running. True when this call started it.</summary>
    Task<bool> StartGrace(Guid userId);

    Task EndGrace(Guid userId);

    /// <summary>Records the "a few days left" notice. True when this call recorded it.</summary>
    Task<bool> MarkGraceWarned(Guid userId);

    Task<List<OwnedClip>> GetOwnedClips(Guid userId);

    /// <summary>Picks (or unpicks) the owner's clip to keep. False when the clip isn't theirs.</summary>
    Task<bool> SetKept(Guid userId, Guid clipId, bool keep);
}

public sealed class PlanEnforcementStatements(NpgsqlConnection connection) : IPlanEnforcementStore
{
    public async Task<List<Guid>> GetAccountsToCheck(int smallestLimit)
    {
        const string sql = """
            SELECT u.id
            FROM app_user u
            WHERE u.deletion_requested_at IS NULL
              AND (u.over_limit_since IS NOT NULL
                   OR (SELECT COUNT(*) FROM clip c WHERE c.owner_id = u.id) > @smallestLimit)
            """;
        return (await connection.QueryAsync<Guid>(sql, new { smallestLimit })).ToList();
    }

    public async Task<OverLimitState> GetState(Guid userId)
    {
        const string sql = """
            SELECT email, over_limit_since, over_limit_warned_at AS warned_at
            FROM app_user
            WHERE id = @userId
            """;
        StateRow? row = await connection.QuerySingleOrDefaultAsync<StateRow>(sql, new { userId });
        return new OverLimitState(row?.Email, row?.OverLimitSince, row?.WarnedAt);
    }

    public async Task<bool> StartGrace(Guid userId)
    {
        const string sql = """
            UPDATE app_user
            SET over_limit_since = now(), over_limit_warned_at = NULL
            WHERE id = @userId AND over_limit_since IS NULL
            """;
        return await connection.ExecuteAsync(sql, new { userId }) == 1;
    }

    public async Task EndGrace(Guid userId)
    {
        const string sql = """
            UPDATE app_user
            SET over_limit_since = NULL, over_limit_warned_at = NULL
            WHERE id = @userId
            """;
        await connection.ExecuteAsync(sql, new { userId });
    }

    public async Task<bool> MarkGraceWarned(Guid userId)
    {
        const string sql = """
            UPDATE app_user
            SET over_limit_warned_at = now()
            WHERE id = @userId AND over_limit_since IS NOT NULL AND over_limit_warned_at IS NULL
            """;
        return await connection.ExecuteAsync(sql, new { userId }) == 1;
    }

    public async Task<List<OwnedClip>> GetOwnedClips(Guid userId)
    {
        const string sql = """
            SELECT c.id, c.title, c.created_at, c.kept_at, g.name AS game_name
            FROM clip c
            LEFT JOIN game_category g ON g.id = c.game_category_id
            WHERE c.owner_id = @userId
            """;
        return (await connection.QueryAsync<OwnedClipRow>(sql, new { userId }))
            .Select(row => new OwnedClip(row.Id, row.Title, row.CreatedAt, row.KeptAt, row.GameName))
            .ToList();
    }

    public async Task<bool> SetKept(Guid userId, Guid clipId, bool keep)
    {
        string sql = keep
            ? "UPDATE clip SET kept_at = COALESCE(kept_at, now()) WHERE id = @clipId AND owner_id = @userId"
            : "UPDATE clip SET kept_at = NULL WHERE id = @clipId AND owner_id = @userId";
        return await connection.ExecuteAsync(sql, new { userId, clipId }) == 1;
    }

    private sealed class StateRow
    {
        public string? Email { get; set; }
        public DateTimeOffset? OverLimitSince { get; set; }
        public DateTimeOffset? WarnedAt { get; set; }
    }

    private sealed class OwnedClipRow
    {
        public Guid Id { get; set; }
        public string? Title { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? KeptAt { get; set; }
        public string? GameName { get; set; }
    }
}
