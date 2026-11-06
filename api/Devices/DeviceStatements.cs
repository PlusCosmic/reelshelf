using System.Security.Cryptography;
using Dapper;
using Npgsql;

namespace Reelshelf.Devices;

/// <summary>A sign-in request as <c>/device</c> shows it.</summary>
public sealed record DeviceAuthorizationRequest(string DeviceName, DateTimeOffset ExpiresAt);

/// <summary>A device signed in to the account, as Settings → Devices lists it.</summary>
public sealed record DeviceSummary(Guid Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset LastUsedAt);

/// <summary>What deciding a sign-in request did.</summary>
public enum DeviceDecisionOutcome
{
    Decided,

    /// <summary>No live request has that code: it never existed, has expired, or its token was issued.</summary>
    NotFound,

    /// <summary>The request was approved or denied already.</summary>
    AlreadyDecided
}

/// <summary>What polling for a token answered (RFC 8628, section 3.5).</summary>
public enum DeviceTokenPollOutcome
{
    Issued,
    AuthorizationPending,
    SlowDown,
    AccessDenied,
    ExpiredToken
}

/// <summary>A token as its scheme reads it: whose it is and when it was last written.</summary>
public sealed class DeviceTokenRow
{
    public Guid Id { get; set; }
    public Guid AppUserId { get; set; }
    public DateTimeOffset LastUsedAt { get; set; }
    public string? ClientVersion { get; set; }
}

/// <summary>The device sign-in tables (V63): <c>device_authorization</c> and <c>device_token</c>.</summary>
public sealed class DeviceStatements(NpgsqlConnection connection)
{
    /// <summary>
    /// Stores a new sign-in request. A user code is unique among stored requests, so one that clashes with a live
    /// request answers false and the caller draws another; an expired request holding it is deleted first.
    /// </summary>
    public async Task<bool> TryCreateAuthorization(Guid id, byte[] deviceCodeHash, string userCode, string deviceName,
        TimeSpan lifetime)
    {
        await connection.ExecuteAsync(
            "DELETE FROM device_authorization WHERE user_code = @userCode AND expires_at <= now()", new { userCode });
        return await connection.ExecuteAsync("""
            INSERT INTO device_authorization (id, device_code_hash, user_code, device_name, expires_at)
            VALUES (@id, @deviceCodeHash, @userCode, @deviceName, now() + @lifetime)
            ON CONFLICT (user_code) DO NOTHING
            """, new { id, deviceCodeHash, userCode, deviceName, lifetime }) == 1;
    }

    /// <summary>A live, undecided request by its stored user code, or null.</summary>
    public async Task<DeviceAuthorizationRequest?> GetPendingAuthorization(string userCode)
    {
        AuthorizationRow? row = await FindLive(userCode);
        return row is null || row.AppUserId is not null || row.DeniedAt is not null
            ? null
            : new DeviceAuthorizationRequest(row.DeviceName, row.ExpiresAt);
    }

    /// <summary>Approves a live request for <paramref name="userId"/>, or denies it when the user is null.</summary>
    public async Task<DeviceDecisionOutcome> Decide(string userCode, Guid? approvedBy)
    {
        int changed = approvedBy is { } userId
            ? await connection.ExecuteAsync("""
                UPDATE device_authorization SET app_user_id = @userId
                WHERE user_code = @userCode AND expires_at > now() AND app_user_id IS NULL AND denied_at IS NULL
                """, new { userCode, userId })
            : await connection.ExecuteAsync("""
                UPDATE device_authorization SET denied_at = now()
                WHERE user_code = @userCode AND expires_at > now() AND app_user_id IS NULL AND denied_at IS NULL
                """, new { userCode });
        if (changed == 1)
        {
            return DeviceDecisionOutcome.Decided;
        }

        return await FindLive(userCode) is null ? DeviceDecisionOutcome.NotFound : DeviceDecisionOutcome.AlreadyDecided;
    }

    /// <summary>
    /// One poll of <c>POST /api/devices/token</c>, under a row lock so two polls of one code can't both get a token:
    /// an approved request becomes a token and is deleted in the same transaction. Every poll moves
    /// <c>last_polled_at</c>, so a client that keeps polling too fast keeps hearing <c>slow_down</c>.
    /// </summary>
    public async Task<(DeviceTokenPollOutcome Outcome, Guid? TokenId)> Poll(byte[] deviceCodeHash, byte[] tokenHash,
        TimeSpan pollInterval)
    {
        await OpenAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
        PollRow? row = await connection.QuerySingleOrDefaultAsync<PollRow>("""
            SELECT id, device_code_hash, device_name, app_user_id, denied_at,
                   expires_at <= now() AS expired,
                   last_polled_at IS NOT NULL AND last_polled_at > now() - @pollInterval AS too_soon
            FROM device_authorization
            WHERE device_code_hash = @deviceCodeHash
            FOR UPDATE
            """, new { deviceCodeHash, pollInterval }, transaction);

        // A code that was never issued, or whose token was issued already, is refused like an expired one: the
        // app has to start again either way. The lookup is by hash; the compare is constant time all the same.
        if (row is null || !CryptographicOperations.FixedTimeEquals(row.DeviceCodeHash, deviceCodeHash) || row.Expired)
        {
            return (DeviceTokenPollOutcome.ExpiredToken, null);
        }

        if (row.DeniedAt is not null)
        {
            return (DeviceTokenPollOutcome.AccessDenied, null);
        }

        await connection.ExecuteAsync("UPDATE device_authorization SET last_polled_at = now() WHERE id = @Id",
            new { row.Id }, transaction);
        if (row.TooSoon)
        {
            await transaction.CommitAsync();
            return (DeviceTokenPollOutcome.SlowDown, null);
        }

        if (row.AppUserId is not { } userId)
        {
            await transaction.CommitAsync();
            return (DeviceTokenPollOutcome.AuthorizationPending, null);
        }

        Guid tokenId = Guid.NewGuid();
        await connection.ExecuteAsync("""
            INSERT INTO device_token (id, app_user_id, name, token_hash) VALUES (@tokenId, @userId, @name, @tokenHash);
            DELETE FROM device_authorization WHERE id = @requestId;
            """, new { tokenId, userId, name = row.DeviceName, tokenHash, requestId = row.Id }, transaction);
        await transaction.CommitAsync();
        return (DeviceTokenPollOutcome.Issued, tokenId);
    }

    /// <summary>
    /// The live token with this hash, or null. A revoked token, or one whose account is suspended or being deleted, is
    /// refused here, so it gets 401 before anything else runs.
    /// </summary>
    public async Task<DeviceTokenRow?> FindToken(byte[] tokenHash)
    {
        return await connection.QuerySingleOrDefaultAsync<DeviceTokenRow>("""
            SELECT t.id, t.app_user_id, t.last_used_at, t.client_version
            FROM device_token t
            JOIN app_user u ON u.id = t.app_user_id
            WHERE t.token_hash = @tokenHash
              AND t.revoked_at IS NULL
              AND u.suspended_at IS NULL
              AND u.deletion_requested_at IS NULL
            """, new { tokenHash });
    }

    public async Task MarkUsed(Guid tokenId, string? clientVersion)
    {
        await connection.ExecuteAsync(
            "UPDATE device_token SET last_used_at = now(), client_version = @clientVersion WHERE id = @tokenId",
            new { tokenId, clientVersion });
    }

    /// <summary>The account's devices that aren't revoked, newest first.</summary>
    public async Task<List<DeviceSummary>> ListDevices(Guid userId)
    {
        return (await connection.QueryAsync<DeviceRow>("""
            SELECT id, name, created_at, last_used_at
            FROM device_token
            WHERE app_user_id = @userId AND revoked_at IS NULL
            ORDER BY created_at DESC, id
            """, new { userId }))
            .Select(row => new DeviceSummary(row.Id, row.Name, row.CreatedAt, row.LastUsedAt))
            .ToList();
    }

    /// <summary>Revokes one of the account's devices; false when it has none by that id that is still signed in.</summary>
    public async Task<bool> Revoke(Guid tokenId, Guid userId)
    {
        return await connection.ExecuteAsync("""
            UPDATE device_token SET revoked_at = now()
            WHERE id = @tokenId AND app_user_id = @userId AND revoked_at IS NULL
            """, new { tokenId, userId }) == 1;
    }

    /// <summary>Deletes sign-in requests past their expiry.</summary>
    public async Task<int> DeleteExpiredAuthorizations()
    {
        return await connection.ExecuteAsync("DELETE FROM device_authorization WHERE expires_at <= now()");
    }

    /// <summary>Revokes tokens unused for <paramref name="unusedFor"/>.</summary>
    public async Task<int> RevokeUnused(TimeSpan unusedFor)
    {
        return await connection.ExecuteAsync("""
            UPDATE device_token SET revoked_at = now()
            WHERE revoked_at IS NULL AND last_used_at < now() - @unusedFor
            """, new { unusedFor });
    }

    private async Task<AuthorizationRow?> FindLive(string userCode)
    {
        return await connection.QuerySingleOrDefaultAsync<AuthorizationRow>("""
            SELECT device_name, app_user_id, denied_at, expires_at
            FROM device_authorization
            WHERE user_code = @userCode AND expires_at > now()
            """, new { userCode });
    }

    private async Task OpenAsync()
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }
    }

    private sealed class AuthorizationRow
    {
        public string DeviceName { get; set; } = "";
        public Guid? AppUserId { get; set; }
        public DateTimeOffset? DeniedAt { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
    }

    private sealed class DeviceRow
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset LastUsedAt { get; set; }
    }

    private sealed class PollRow
    {
        public Guid Id { get; set; }
        public byte[] DeviceCodeHash { get; set; } = [];
        public string DeviceName { get; set; } = "";
        public Guid? AppUserId { get; set; }
        public DateTimeOffset? DeniedAt { get; set; }
        public bool Expired { get; set; }
        public bool TooSoon { get; set; }
    }
}
