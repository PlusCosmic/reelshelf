using System.Text.Json;
using Dapper;
using Npgsql;
using Reelshelf.Video;

namespace Reelshelf.Plans;

public static class ClipExportReasons
{
    /// <summary>The owner asked for a copy of their clips over the limit.</summary>
    public const string Requested = "requested";

    /// <summary>A grace ended; its clips are deleted once this export is ready.</summary>
    public const string GraceEnded = "grace_ended";
}

public static class ClipExportStatuses
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Ready = "ready";
    public const string Failed = "failed";
    public const string Expired = "expired";

    /// <summary>Still to be made, or being made.</summary>
    public static bool IsActive(string status) => status is Pending or Running;
}

/// <summary>
/// One zip of an export: <c>exports/{userId}/{exportId}/{leaseId}/reelshelf-clips-{index}.zip</c>, under the lease of
/// the run that made it (<see cref="ExportParts.Key(ClipExport, int)"/>).
/// </summary>
public sealed record ClipExportPart(int Index, long SizeBytes, int ClipCount);

public sealed record ClipExport(
    Guid Id,
    Guid UserId,
    string Reason,
    string Status,
    IReadOnlyList<Guid> ClipIds,
    IReadOnlyList<ClipExportPart> Parts,
    int Attempts,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ExpiresAt,
    Guid? LeaseId = null);

/// <summary>A clip going into an export, with what's needed to fetch its video and name it in the zip.</summary>
public sealed class ExportClipRow : IClipVideoColumns
{
    public Guid Id { get; set; }
    public string? Title { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? GameName { get; set; }
    public long? StorageSize { get; set; }
    public string VideoHost { get; set; } = VideoHostNames.Bunny;
    public Guid VideoId { get; set; }
    public int VideoVersion { get; set; } = 1;
    public string? VideoCodecs { get; set; }
    public int? VideoHeight { get; set; }
    public string VideoState { get; set; } = ClipVideoStatus.AwaitingUpload.ToDatabase();
    public ClipVideoStatus VideoStatus => ClipVideoStatuses.FromDatabase(VideoState);
    public bool ThumbnailsReady => false;
}

public interface IClipExportStore
{
    /// <summary>
    /// Queues an export, or returns null when the account already has one of that <paramref name="reason"/> waiting or
    /// being made (one at a time, by a unique index).
    /// </summary>
    Task<Guid?> Create(Guid userId, string reason, IReadOnlyCollection<Guid> clipIds);
    Task<List<ClipExport>> GetForUser(Guid userId);
    Task<ClipExport?> Get(Guid exportId);

    /// <summary>
    /// Claims the oldest export waiting to be made, or one whose maker went quiet for <paramref name="staleAfter"/>,
    /// counts the attempt and gives it a new <see cref="ClipExport.LeaseId"/>. Exports that have used
    /// <paramref name="maxAttempts"/> (a maker that went quiet included) are failed instead. Null when there is none.
    /// </summary>
    Task<ClipExport?> Claim(TimeSpan staleAfter, int maxAttempts);

    /// <summary>False when <paramref name="leaseId"/> no longer holds the export (it was reclaimed or settled).</summary>
    Task<bool> Heartbeat(Guid exportId, Guid leaseId);

    /// <summary>Marks the export ready, only while <paramref name="leaseId"/> still holds it; false otherwise.</summary>
    Task<bool> Complete(Guid exportId, Guid leaseId, IReadOnlyList<ClipExportPart> parts, DateTimeOffset expiresAt);

    /// <summary>
    /// Records a failed attempt: back to pending after <paramref name="retryAfter"/>, or failed for good when null.
    /// Only while <paramref name="leaseId"/> still holds the export; false otherwise.
    /// </summary>
    Task<bool> Fail(Guid exportId, Guid leaseId, string error, TimeSpan? retryAfter);

    /// <summary>Marks ready exports past their expiry as expired and returns them, so their objects can be deleted.</summary>
    Task<List<ClipExport>> ExpireDue();

    /// <summary>The clips of <paramref name="clipIds"/> the owner still has, oldest first.</summary>
    Task<List<ExportClipRow>> GetExportClips(Guid userId, IReadOnlyCollection<Guid> clipIds);
}

public sealed class ClipExportStatements(NpgsqlConnection connection) : IClipExportStore
{
    private const string Columns =
        "id, user_id, reason, status, clip_ids, parts::text AS parts, attempts, error, created_at, completed_at, expires_at, " +
        "lease_id";

    private static readonly JsonSerializerOptions PartsJson = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public async Task<Guid?> Create(Guid userId, string reason, IReadOnlyCollection<Guid> clipIds)
    {
        // clip_export_active_idx (V58): one waiting or running export per account and reason.
        const string sql = """
            INSERT INTO clip_export (user_id, reason, clip_ids)
            VALUES (@userId, @reason, @clipIds)
            ON CONFLICT (user_id, reason) WHERE status IN ('pending', 'running') DO NOTHING
            RETURNING id
            """;
        return await connection.QuerySingleOrDefaultAsync<Guid?>(sql,
            new { userId, reason, clipIds = clipIds.ToArray() });
    }

    public async Task<List<ClipExport>> GetForUser(Guid userId)
    {
        string sql = $"SELECT {Columns} FROM clip_export WHERE user_id = @userId ORDER BY created_at DESC";
        return (await connection.QueryAsync<ExportRow>(sql, new { userId })).Select(row => row.ToExport()).ToList();
    }

    public async Task<ClipExport?> Get(Guid exportId)
    {
        string sql = $"SELECT {Columns} FROM clip_export WHERE id = @exportId";
        return (await connection.QuerySingleOrDefaultAsync<ExportRow>(sql, new { exportId }))?.ToExport();
    }

    public async Task<ClipExport?> Claim(TimeSpan staleAfter, int maxAttempts)
    {
        // A maker that went quiet used its attempt too, so an export that keeps crashing or hanging its maker runs out.
        await connection.ExecuteAsync("""
            UPDATE clip_export
            SET status = 'failed',
                error = COALESCE(error, 'The export stopped responding'),
                lease_id = NULL,
                heartbeat_at = NULL,
                completed_at = now()
            WHERE attempts >= @maxAttempts
              AND ((status = 'pending' AND available_at <= now())
                OR (status = 'running' AND heartbeat_at < now() - @staleAfter))
            """, new { staleAfter, maxAttempts });

        string sql = $"""
            UPDATE clip_export
            SET status = 'running', attempts = attempts + 1, heartbeat_at = now(), lease_id = gen_random_uuid()
            WHERE id = (
                SELECT id FROM clip_export
                WHERE attempts < @maxAttempts
                  AND ((status = 'pending' AND available_at <= now())
                    OR (status = 'running' AND heartbeat_at < now() - @staleAfter))
                ORDER BY created_at
                LIMIT 1
                FOR UPDATE SKIP LOCKED)
            RETURNING {Columns}
            """;
        return (await connection.QuerySingleOrDefaultAsync<ExportRow>(sql, new { staleAfter, maxAttempts }))
            ?.ToExport();
    }

    public async Task<bool> Heartbeat(Guid exportId, Guid leaseId)
    {
        const string sql = """
            UPDATE clip_export SET heartbeat_at = now()
            WHERE id = @exportId AND status = 'running' AND lease_id = @leaseId
            """;
        return await connection.ExecuteAsync(sql, new { exportId, leaseId }) == 1;
    }

    public async Task<bool> Complete(Guid exportId, Guid leaseId, IReadOnlyList<ClipExportPart> parts,
        DateTimeOffset expiresAt)
    {
        // The lease stays on the ready export: it names the folder its parts are in.
        const string sql = """
            UPDATE clip_export
            SET status = 'ready', parts = @parts::jsonb, error = NULL, heartbeat_at = NULL, completed_at = now(),
                expires_at = @expiresAt
            WHERE id = @exportId AND status = 'running' AND lease_id = @leaseId
            """;
        return await connection.ExecuteAsync(sql,
            new { exportId, leaseId, parts = JsonSerializer.Serialize(parts, PartsJson), expiresAt }) == 1;
    }

    public async Task<bool> Fail(Guid exportId, Guid leaseId, string error, TimeSpan? retryAfter)
    {
        const string sql = """
            UPDATE clip_export
            SET status = CASE WHEN @retry THEN 'pending' ELSE 'failed' END,
                available_at = CASE WHEN @retry THEN now() + @retryAfter ELSE available_at END,
                error = @error,
                lease_id = NULL,
                heartbeat_at = NULL,
                completed_at = CASE WHEN @retry THEN NULL ELSE now() END
            WHERE id = @exportId AND status = 'running' AND lease_id = @leaseId
            """;
        return await connection.ExecuteAsync(sql, new
        {
            exportId, leaseId, error, retry = retryAfter is not null, retryAfter = retryAfter ?? TimeSpan.Zero
        }) == 1;
    }

    public async Task<List<ClipExport>> ExpireDue()
    {
        string sql = $"""
            UPDATE clip_export
            SET status = 'expired'
            WHERE status = 'ready' AND expires_at < now()
            RETURNING {Columns}
            """;
        return (await connection.QueryAsync<ExportRow>(sql)).Select(row => row.ToExport()).ToList();
    }

    public async Task<List<ExportClipRow>> GetExportClips(Guid userId, IReadOnlyCollection<Guid> clipIds)
    {
        const string sql = """
            SELECT c.id, c.title, c.created_at, g.name AS game_name, c.storage_size,
                   c.video_host, c.video_id, c.video_version, c.video_codecs, c.video_height, c.video_state
            FROM clip c
            LEFT JOIN game_category g ON g.id = c.game_category_id
            WHERE c.owner_id = @userId AND c.id = ANY(@clipIds)
            ORDER BY c.created_at, c.id
            """;
        return (await connection.QueryAsync<ExportClipRow>(sql, new { userId, clipIds = clipIds.ToArray() })).ToList();
    }

    private sealed class ExportRow
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string Reason { get; set; } = "";
        public string Status { get; set; } = "";
        public Guid[] ClipIds { get; set; } = [];
        public string Parts { get; set; } = "[]";
        public int Attempts { get; set; }
        public string? Error { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public Guid? LeaseId { get; set; }

        public ClipExport ToExport() => new(Id, UserId, Reason, Status, ClipIds,
            JsonSerializer.Deserialize<List<ClipExportPart>>(Parts, PartsJson) ?? [], Attempts, Error, CreatedAt,
            CompletedAt, ExpiresAt, LeaseId);
    }
}
