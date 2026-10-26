using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Npgsql;
using Reelshelf.Video;

namespace Reelshelf.Moderation;

public static class ClipEvidenceStatuses
{
    public const string Pending = "pending";
    public const string Preserved = "preserved";
    public const string Failed = "failed";
}

/// <summary>A removed clip's evidence record (<c>clip_evidence</c>), as the processor works on it.</summary>
public sealed class ClipEvidence
{
    public Guid Id { get; set; }
    public Guid ClipId { get; set; }
    public Guid? OwnerId { get; set; }
    public string Reason { get; set; } = "";
    public string Note { get; set; } = "";
    public DateTimeOffset RemovedAt { get; set; }
    public string Clip { get; set; } = "{}";
    public string Owner { get; set; } = "{}";
    public string Status { get; set; } = ClipEvidenceStatuses.Pending;
    public int Attempts { get; set; }
    public string? Files { get; set; }
    public DateTimeOffset? MediaDeletedAt { get; set; }
    public string? CdnPurge { get; set; }
    public DateTimeOffset DeleteAfter { get; set; }

    public EvidenceClip ReadClip() => EvidenceClip.Parse(Clip);
}

/// <summary>What the processor needs from the clip row snapshotted into <c>clip_evidence.clip</c>.</summary>
public sealed class EvidenceClip
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public Guid Id { get; set; }
    public string? Title { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? GameName { get; set; }
    public string VideoHost { get; set; } = VideoHostNames.Bunny;
    public Guid VideoId { get; set; }
    public int VideoVersion { get; set; } = 1;
    public string? VideoCodecs { get; set; }
    public int? VideoHeight { get; set; }
    public string VideoState { get; set; } = ClipVideoStatus.AwaitingUpload.ToDatabase();
    public Guid? BunnyVideoId { get; set; }
    public long? StorageSize { get; set; }

    [JsonIgnore]
    public ClipVideoStatus VideoStatus => ClipVideoStatuses.FromDatabase(VideoState);

    public static EvidenceClip Parse(string json) =>
        JsonSerializer.Deserialize<EvidenceClip>(json, JsonOptions)
        ?? throw new InvalidOperationException("The evidence record has no clip");
}

/// <summary>The evidence records the processor claims, works through and finally deletes.</summary>
public interface IClipEvidenceStore
{
    /// <summary>
    /// Fails records out of attempts, then claims the oldest pending one past its retry delay, or one whose claim went
    /// stale; null when there is none.
    /// </summary>
    Task<ClipEvidence?> Claim(TimeSpan staleAfter, int maxAttempts);

    /// <summary>Claims a preserved record whose second sweep is due.</summary>
    Task<ClipEvidence?> ClaimSweep(TimeSpan sweepAfter, TimeSpan staleAfter);

    Task SaveFiles(Guid id, IReadOnlyList<string> keys);
    Task MarkMediaDeleted(Guid id);
    Task SaveCdnPurge(Guid id, string result);
    Task MarkPreserved(Guid id);
    Task MarkSwept(Guid id);

    /// <summary>Puts the record back to be retried after <paramref name="retryAfter"/>.</summary>
    Task Fail(Guid id, string error, TimeSpan retryAfter);

    /// <summary>Records whose retention is over.</summary>
    Task<List<Guid>> GetExpired(int limit);

    Task Delete(Guid id);
}

public sealed class ClipEvidenceStatements(NpgsqlConnection connection) : IClipEvidenceStore
{
    private const string Columns = """
        id, clip_id, owner_id, reason, note, removed_at, clip::text AS clip, owner::text AS owner, status, attempts,
        files::text AS files, media_deleted_at, cdn_purge, delete_after
        """;

    public async Task<ClipEvidence?> Claim(TimeSpan staleAfter, int maxAttempts)
    {
        await connection.ExecuteAsync("""
            UPDATE clip_evidence SET status = 'failed', claimed_at = NULL
            WHERE status = 'pending' AND attempts >= @maxAttempts
              AND (claimed_at IS NULL OR claimed_at < now() - @staleAfter)
            """, new { maxAttempts, staleAfter });

        return await connection.QuerySingleOrDefaultAsync<ClipEvidence>($"""
            UPDATE clip_evidence SET claimed_at = now(), attempts = attempts + 1
            WHERE id = (
                SELECT id FROM clip_evidence
                WHERE status = 'pending' AND available_at <= now() AND attempts < @maxAttempts
                  AND (claimed_at IS NULL OR claimed_at < now() - @staleAfter)
                ORDER BY available_at
                LIMIT 1
                FOR UPDATE SKIP LOCKED)
            RETURNING {Columns}
            """, new { maxAttempts, staleAfter });
    }

    public async Task<ClipEvidence?> ClaimSweep(TimeSpan sweepAfter, TimeSpan staleAfter)
    {
        return await connection.QuerySingleOrDefaultAsync<ClipEvidence>($"""
            UPDATE clip_evidence SET claimed_at = now()
            WHERE id = (
                SELECT id FROM clip_evidence
                WHERE status = 'preserved' AND swept_at IS NULL AND removed_at < now() - @sweepAfter
                  AND (claimed_at IS NULL OR claimed_at < now() - @staleAfter)
                ORDER BY removed_at
                LIMIT 1
                FOR UPDATE SKIP LOCKED)
            RETURNING {Columns}
            """, new { sweepAfter, staleAfter });
    }

    public Task SaveFiles(Guid id, IReadOnlyList<string> keys) =>
        connection.ExecuteAsync("UPDATE clip_evidence SET files = @files::jsonb WHERE id = @id",
            new { id, files = JsonSerializer.Serialize(keys) });

    public Task MarkMediaDeleted(Guid id) =>
        connection.ExecuteAsync("UPDATE clip_evidence SET media_deleted_at = now() WHERE id = @id", new { id });

    public Task SaveCdnPurge(Guid id, string result) =>
        connection.ExecuteAsync("UPDATE clip_evidence SET cdn_purge = @result WHERE id = @id", new { id, result });

    public Task MarkPreserved(Guid id) =>
        connection.ExecuteAsync("""
            UPDATE clip_evidence SET status = 'preserved', claimed_at = NULL, last_error = NULL WHERE id = @id
            """, new { id });

    public Task MarkSwept(Guid id) =>
        connection.ExecuteAsync("UPDATE clip_evidence SET swept_at = now(), claimed_at = NULL WHERE id = @id",
            new { id });

    public Task Fail(Guid id, string error, TimeSpan retryAfter) =>
        connection.ExecuteAsync("""
            UPDATE clip_evidence
            SET claimed_at = NULL, last_error = @error, available_at = now() + @retryAfter
            WHERE id = @id
            """, new { id, error, retryAfter });

    public async Task<List<Guid>> GetExpired(int limit) =>
        (await connection.QueryAsync<Guid>(
            "SELECT id FROM clip_evidence WHERE delete_after < now() ORDER BY delete_after LIMIT @limit",
            new { limit })).ToList();

    public Task Delete(Guid id) =>
        connection.ExecuteAsync("DELETE FROM clip_evidence WHERE id = @id", new { id });
}
