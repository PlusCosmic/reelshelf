using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Npgsql;
using Reelshelf.Video;

namespace Reelshelf.Moderation;

public static class ClipEvidenceStatuses
{
    /// <summary>The evidence copy isn't made yet; the processor is working on it.</summary>
    public const string Pending = "pending";

    /// <summary>The evidence copy exists, and the clip's video is gone from its host and the quarantine.</summary>
    public const string Preserved = "preserved";

    /// <summary>
    /// Needs attention: the copy still isn't made after <c>EvidenceMaxAttempts</c>. It is never given up on (retried
    /// with a capped back-off) and nothing of the clip is deleted until it is made; an R2 clip's files stay in the
    /// quarantine, off public access, and a Bunny clip's video stays at Bunny.
    /// </summary>
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
    public DateTimeOffset? ClaimedAt { get; set; }
    public string? LastError { get; set; }

    /// <summary>The claim this record was handed out under, which every step write carries.</summary>
    public ClipEvidenceClaim ClaimToken => new(Id,
        ClaimedAt ?? throw new InvalidOperationException("The evidence record isn't claimed"));

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

/// <summary>
/// Who holds a record: its id and the <c>claimed_at</c> its claim set. Step writes carry it, so a processor whose
/// claim went stale and was taken over by another writes nothing.
/// </summary>
public readonly record struct ClipEvidenceClaim(Guid Id, DateTimeOffset ClaimedAt);

/// <summary>The evidence records the processor claims, works through and finally deletes.</summary>
public interface IClipEvidenceStore
{
    /// <summary>
    /// Claims the oldest record not yet preserved (<c>pending</c>, or <c>failed</c> and needing attention) past its
    /// retry delay, or one whose claim went stale; null when there is none. There is no limit on attempts.
    /// </summary>
    Task<ClipEvidence?> Claim(TimeSpan staleAfter);

    /// <summary>
    /// Claims a record, of any status, whose quarantine step is done (<c>cdn_purge</c>) and whose second sweep is due.
    /// </summary>
    Task<ClipEvidence?> ClaimSweep(TimeSpan sweepAfter, TimeSpan staleAfter);

    // Each step write is dropped, answering false, when the claim is no longer the record's current one.
    Task<bool> SaveCdnPurge(ClipEvidenceClaim claim, string result);
    Task<bool> SaveFiles(ClipEvidenceClaim claim, IReadOnlyList<string> keys);
    Task<bool> MarkMediaDeleted(ClipEvidenceClaim claim);
    Task<bool> MarkPreserved(ClipEvidenceClaim claim);
    Task<bool> MarkSwept(ClipEvidenceClaim claim);

    /// <summary>
    /// Puts the record back to be retried after <paramref name="retryAfter"/>; with <paramref name="needsAttention"/>,
    /// a pending record becomes <c>failed</c>, which is still retried.
    /// </summary>
    Task<bool> Fail(ClipEvidenceClaim claim, string error, TimeSpan retryAfter, bool needsAttention = false);

    /// <summary>
    /// Records whose retention is over and that are preserved: never one whose evidence copy doesn't exist yet, or
    /// whose video isn't yet deleted, since the record is the only pointer left to that video.
    /// </summary>
    Task<List<Guid>> GetExpired(int limit);

    Task Delete(Guid id);
}

public sealed class ClipEvidenceStatements(NpgsqlConnection connection) : IClipEvidenceStore
{
    private const string Columns = """
        id, clip_id, owner_id, reason, note, removed_at, clip::text AS clip, owner::text AS owner, status, attempts,
        files::text AS files, media_deleted_at, cdn_purge, delete_after, claimed_at, last_error
        """;

    /// <summary>Matches the record only while the claim is its current one.</summary>
    private const string Claimed = "id = @Id AND claimed_at = @ClaimedAt";

    public async Task<ClipEvidence?> Claim(TimeSpan staleAfter)
    {
        return await connection.QuerySingleOrDefaultAsync<ClipEvidence>($"""
            UPDATE clip_evidence SET claimed_at = now(), attempts = attempts + 1
            WHERE id = (
                SELECT id FROM clip_evidence
                WHERE status IN ('pending', 'failed') AND available_at <= now()
                  AND (claimed_at IS NULL OR claimed_at < now() - @staleAfter)
                ORDER BY available_at
                LIMIT 1
                FOR UPDATE SKIP LOCKED)
            RETURNING {Columns}
            """, new { staleAfter });
    }

    public async Task<ClipEvidence?> ClaimSweep(TimeSpan sweepAfter, TimeSpan staleAfter)
    {
        return await connection.QuerySingleOrDefaultAsync<ClipEvidence>($"""
            UPDATE clip_evidence SET claimed_at = now()
            WHERE id = (
                SELECT id FROM clip_evidence
                WHERE cdn_purge IS NOT NULL AND swept_at IS NULL AND removed_at < now() - @sweepAfter
                  AND available_at <= now()
                  AND (claimed_at IS NULL OR claimed_at < now() - @staleAfter)
                ORDER BY removed_at
                LIMIT 1
                FOR UPDATE SKIP LOCKED)
            RETURNING {Columns}
            """, new { sweepAfter, staleAfter });
    }

    public async Task<bool> SaveFiles(ClipEvidenceClaim claim, IReadOnlyList<string> keys) =>
        await connection.ExecuteAsync($"UPDATE clip_evidence SET files = @files::jsonb WHERE {Claimed}",
            new { claim.Id, claim.ClaimedAt, files = JsonSerializer.Serialize(keys) }) > 0;

    public async Task<bool> MarkMediaDeleted(ClipEvidenceClaim claim) =>
        await connection.ExecuteAsync($"UPDATE clip_evidence SET media_deleted_at = now() WHERE {Claimed}",
            new { claim.Id, claim.ClaimedAt }) > 0;

    public async Task<bool> SaveCdnPurge(ClipEvidenceClaim claim, string result) =>
        await connection.ExecuteAsync($"UPDATE clip_evidence SET cdn_purge = @result WHERE {Claimed}",
            new { claim.Id, claim.ClaimedAt, result }) > 0;

    public async Task<bool> MarkPreserved(ClipEvidenceClaim claim) =>
        await connection.ExecuteAsync($"""
            UPDATE clip_evidence SET status = 'preserved', claimed_at = NULL, last_error = NULL
            WHERE {Claimed} AND status IN ('pending', 'failed') AND files IS NOT NULL AND media_deleted_at IS NOT NULL
            """, new { claim.Id, claim.ClaimedAt }) > 0;

    public async Task<bool> MarkSwept(ClipEvidenceClaim claim) =>
        await connection.ExecuteAsync($"UPDATE clip_evidence SET swept_at = now(), claimed_at = NULL WHERE {Claimed}",
            new { claim.Id, claim.ClaimedAt }) > 0;

    public async Task<bool> Fail(ClipEvidenceClaim claim, string error, TimeSpan retryAfter,
        bool needsAttention = false) =>
        await connection.ExecuteAsync($"""
            UPDATE clip_evidence
            SET claimed_at = NULL, last_error = @error, available_at = now() + @retryAfter,
                status = CASE WHEN @needsAttention AND status = 'pending' THEN 'failed' ELSE status END
            WHERE {Claimed}
            """, new { claim.Id, claim.ClaimedAt, error, retryAfter, needsAttention }) > 0;

    public async Task<List<Guid>> GetExpired(int limit) =>
        (await connection.QueryAsync<Guid>("""
            SELECT id FROM clip_evidence
            WHERE delete_after < now() AND status = 'preserved'
              AND files IS NOT NULL AND media_deleted_at IS NOT NULL
            ORDER BY delete_after
            LIMIT @limit
            """, new { limit })).ToList();

    public Task Delete(Guid id) =>
        connection.ExecuteAsync("DELETE FROM clip_evidence WHERE id = @id", new { id });
}

