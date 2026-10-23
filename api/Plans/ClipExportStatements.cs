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

/// <summary>One zip of an export: <c>exports/{userId}/{exportId}/reelshelf-clips-{index}.zip</c>.</summary>
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
    DateTimeOffset? ExpiresAt);

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
    Task<Guid> Create(Guid userId, string reason, IReadOnlyCollection<Guid> clipIds);
    Task<List<ClipExport>> GetForUser(Guid userId);
    Task<ClipExport?> Get(Guid exportId);

    /// <summary>
    /// Claims the oldest export waiting to be made, or one whose maker went quiet for <paramref name="staleAfter"/>,
    /// and counts the attempt. Null when there is none.
    /// </summary>
    Task<ClipExport?> Claim(TimeSpan staleAfter);

    Task Heartbeat(Guid exportId);
    Task Complete(Guid exportId, IReadOnlyList<ClipExportPart> parts, DateTimeOffset expiresAt);

    /// <summary>Records a failed attempt: back to pending after <paramref name="retryAfter"/>, or failed for good when null.</summary>
    Task Fail(Guid exportId, string error, TimeSpan? retryAfter);

    /// <summary>Marks ready exports past their expiry as expired and returns them, so their objects can be deleted.</summary>
    Task<List<ClipExport>> ExpireDue();

    /// <summary>The clips of <paramref name="clipIds"/> the owner still has, oldest first.</summary>
    Task<List<ExportClipRow>> GetExportClips(Guid userId, IReadOnlyCollection<Guid> clipIds);
}

public sealed class ClipExportStatements(NpgsqlConnection connection) : IClipExportStore
{
    private const string Columns =
        "id, user_id, reason, status, clip_ids, parts::text AS parts, attempts, error, created_at, completed_at, expires_at";

    private static readonly JsonSerializerOptions PartsJson = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public async Task<Guid> Create(Guid userId, string reason, IReadOnlyCollection<Guid> clipIds)
    {
        const string sql = """
            INSERT INTO clip_export (user_id, reason, clip_ids)
            VALUES (@userId, @reason, @clipIds)
            RETURNING id
            """;
        return await connection.QuerySingleAsync<Guid>(sql, new { userId, reason, clipIds = clipIds.ToArray() });
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

    public async Task<ClipExport?> Claim(TimeSpan staleAfter)
    {
        string sql = $"""
            UPDATE clip_export
            SET status = 'running', attempts = attempts + 1, heartbeat_at = now()
            WHERE id = (
                SELECT id FROM clip_export
                WHERE (status = 'pending' AND available_at <= now())
                   OR (status = 'running' AND heartbeat_at < now() - @staleAfter)
                ORDER BY created_at
                LIMIT 1
                FOR UPDATE SKIP LOCKED)
            RETURNING {Columns}
            """;
        return (await connection.QuerySingleOrDefaultAsync<ExportRow>(sql, new { staleAfter }))?.ToExport();
    }

    public async Task Heartbeat(Guid exportId)
    {
        await connection.ExecuteAsync("UPDATE clip_export SET heartbeat_at = now() WHERE id = @exportId",
            new { exportId });
    }

    public async Task Complete(Guid exportId, IReadOnlyList<ClipExportPart> parts, DateTimeOffset expiresAt)
    {
        const string sql = """
            UPDATE clip_export
            SET status = 'ready', parts = @parts::jsonb, error = NULL, completed_at = now(), expires_at = @expiresAt
            WHERE id = @exportId
            """;
        await connection.ExecuteAsync(sql,
            new { exportId, parts = JsonSerializer.Serialize(parts, PartsJson), expiresAt });
    }

    public async Task Fail(Guid exportId, string error, TimeSpan? retryAfter)
    {
        const string sql = """
            UPDATE clip_export
            SET status = CASE WHEN @retry THEN 'pending' ELSE 'failed' END,
                available_at = CASE WHEN @retry THEN now() + @retryAfter ELSE available_at END,
                error = @error,
                heartbeat_at = NULL,
                completed_at = CASE WHEN @retry THEN NULL ELSE now() END
            WHERE id = @exportId
            """;
        await connection.ExecuteAsync(sql,
            new { exportId, error, retry = retryAfter is not null, retryAfter = retryAfter ?? TimeSpan.Zero });
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

        public ClipExport ToExport() => new(Id, UserId, Reason, Status, ClipIds,
            JsonSerializer.Deserialize<List<ClipExportPart>>(Parts, PartsJson) ?? [], Attempts, Error, CreatedAt,
            CompletedAt, ExpiresAt);
    }
}
