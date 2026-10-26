using Dapper;
using Npgsql;

namespace Reelshelf.Moderation;

public static class ContentReportStatuses
{
    public const string Open = "open";
    public const string TakenDown = "taken_down";
    public const string Dismissed = "dismissed";
}

/// <summary>The clip a share token leads to, for filing a report against it.</summary>
public sealed class ReportedShare
{
    public Guid ClipId { get; set; }
    public Guid OwnerId { get; set; }
    public string? Title { get; set; }
}

public sealed record NewContentReport(
    Guid ClipId,
    Guid OwnerId,
    string? ClipTitle,
    string ShareToken,
    string Reason,
    string Details,
    string? ReporterEmail);

/// <summary>A report as the admin queue shows it, with the clip's state now.</summary>
public sealed class ContentReportRow
{
    public Guid Id { get; set; }
    public string Reason { get; set; } = "";
    public string Details { get; set; } = "";
    public string? ReporterEmail { get; set; }
    public string Status { get; set; } = ContentReportStatuses.Open;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? ResolvedByName { get; set; }
    public string? ResolutionNote { get; set; }
    public Guid? ClipId { get; set; }
    public string? ClipTitle { get; set; }
    public Guid? ClipOwnerId { get; set; }
    public string? OwnerName { get; set; }

    /// <summary>The clip's live share token, if it has one that anyone can watch now.</summary>
    public string? ActiveShareToken { get; set; }

    /// <summary>The clip has a share link, hidden while a child-safety or terrorism report on it is open.</summary>
    public bool ShareHidden { get; set; }

    public bool SharingBlocked { get; set; }

    /// <summary>Open reports against the same clip, this one included.</summary>
    public int OpenReportsOnClip { get; set; }
}

public enum ResolveOutcome
{
    Resolved,
    NotFound,

    /// <summary>The report was already taken down or dismissed.</summary>
    AlreadyResolved
}

/// <summary>A takedown that went through, with what the owner's notice needs.</summary>
public sealed record TakeDownResult(ResolveOutcome Outcome, Guid? ClipId, string? ClipTitle, string? OwnerEmail,
    string Reason);

/// <summary>
/// Reports of shared clips and the takedowns they lead to. Taking a clip down blocks sharing it and revokes its share
/// links; the clip itself stays in its owner's library.
/// </summary>
public sealed class ContentReportStatements(NpgsqlConnection connection)
{
    /// <summary>
    /// Whether a clip may be seen through a share link: it hasn't been taken down, and isn't waiting on review of an
    /// <see cref="ContentReportReasons.Urgent"/> report.
    /// </summary>
    public static string ShareableSql(string clipAlias) => $"""
        {clipAlias}.sharing_blocked_at IS NULL
        AND NOT EXISTS (
            SELECT 1 FROM content_report urgent_report
            WHERE urgent_report.clip_id = {clipAlias}.id
              AND urgent_report.status = 'open'
              AND urgent_report.reason IN ({ContentReportReasons.UrgentSqlList}))
        """;

    private static readonly string ReportColumns = $"""
        r.id, r.reason, r.details, r.reporter_email, r.status, r.created_at, r.resolved_at,
        resolver.username AS resolved_by_name, r.resolution_note, r.clip_id, r.clip_title, r.clip_owner_id,
        coalesce(owner.global_name, owner.username) AS owner_name,
        (SELECT cs.token FROM clip_share cs
         WHERE cs.clip_id = r.clip_id AND cs.revoked_at IS NULL AND {ShareableSql("c")} LIMIT 1)
            AS active_share_token,
        coalesce(NOT ({ShareableSql("c")}) AND c.sharing_blocked_at IS NULL AND EXISTS (
            SELECT 1 FROM clip_share cs WHERE cs.clip_id = r.clip_id AND cs.revoked_at IS NULL), false) AS share_hidden,
        coalesce(c.sharing_blocked_at IS NOT NULL, false) AS sharing_blocked,
        (SELECT count(*)::int FROM content_report other
         WHERE other.clip_id = r.clip_id AND other.status = 'open') AS open_reports_on_clip
        FROM content_report r
        LEFT JOIN clip c ON c.id = r.clip_id
        LEFT JOIN app_user owner ON owner.id = r.clip_owner_id
        LEFT JOIN app_user resolver ON resolver.id = r.resolved_by
        """;

    /// <summary>The clip behind a share link anyone can watch now, or null.</summary>
    public async Task<ReportedShare?> FindShare(string token)
    {
        string sql = $"""
            SELECT c.id AS clip_id, c.owner_id, c.title
            FROM clip_share cs
            JOIN clip c ON c.id = cs.clip_id
            WHERE cs.token = @token AND cs.revoked_at IS NULL AND {ShareableSql("c")}
            """;
        return await connection.QuerySingleOrDefaultAsync<ReportedShare>(sql, new { token });
    }

    public async Task<Guid> Insert(NewContentReport report)
    {
        return await connection.QuerySingleAsync<Guid>("""
            INSERT INTO content_report (clip_id, clip_owner_id, clip_title, share_token, reason, details, reporter_email)
            VALUES (@ClipId, @OwnerId, @ClipTitle, @ShareToken, @Reason, @Details, @ReporterEmail)
            RETURNING id
            """, report);
    }

    /// <summary>Open reports oldest first, or resolved ones most recently resolved first.</summary>
    public async Task<List<ContentReportRow>> List(bool open, int limit)
    {
        string sql = open
            ? $"SELECT {ReportColumns} WHERE r.status = 'open' ORDER BY r.created_at LIMIT @limit"
            : $"SELECT {ReportColumns} WHERE r.status <> 'open' ORDER BY r.resolved_at DESC LIMIT @limit";
        return (await connection.QueryAsync<ContentReportRow>(sql, new { limit })).ToList();
    }

    public async Task<ContentReportRow?> Get(Guid id)
    {
        return await connection.QuerySingleOrDefaultAsync<ContentReportRow>(
            $"SELECT {ReportColumns} WHERE r.id = @id", new { id });
    }

    /// <summary>
    /// Takes the report's clip down: blocks sharing it, revokes its share links, and resolves every open report against
    /// it, in one transaction. A report whose clip was deleted is resolved alone.
    /// </summary>
    public async Task<TakeDownResult> TakeDown(Guid id, Guid adminId, string note)
    {
        await OpenAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();

        LockedReport? found = await connection.QuerySingleOrDefaultAsync<LockedReport>(
            "SELECT status, clip_id, reason FROM content_report WHERE id = @id FOR UPDATE",
            new { id }, transaction);
        if (found is null)
        {
            return new TakeDownResult(ResolveOutcome.NotFound, null, null, null, "");
        }

        if (found.Status != ContentReportStatuses.Open)
        {
            return new TakeDownResult(ResolveOutcome.AlreadyResolved, found.ClipId, null, null, found.Reason);
        }

        TakenDownClip? clip = null;
        if (found.ClipId is { } clipId)
        {
            clip = await connection.QuerySingleOrDefaultAsync<TakenDownClip>("""
                UPDATE clip SET sharing_blocked_at = coalesce(sharing_blocked_at, now())
                FROM app_user owner
                WHERE clip.id = @clipId AND owner.id = clip.owner_id
                RETURNING clip.title, owner.email
                """, new { clipId }, transaction);
            await connection.ExecuteAsync(
                "UPDATE clip_share SET revoked_at = now() WHERE clip_id = @clipId AND revoked_at IS NULL",
                new { clipId }, transaction);
        }

        await connection.ExecuteAsync("""
            UPDATE content_report
            SET status = 'taken_down', resolved_by = @adminId, resolution_note = @note, resolved_at = now()
            WHERE status = 'open' AND (id = @id OR (@clipId::uuid IS NOT NULL AND clip_id = @clipId))
            """, new { id, clipId = found.ClipId, adminId, note }, transaction);
        await transaction.CommitAsync();

        return new TakeDownResult(ResolveOutcome.Resolved, found.ClipId, clip?.Title, clip?.Email, found.Reason);
    }

    /// <summary>Dismisses one open report; the clip, and any other report against it, are left as they are.</summary>
    public async Task<ResolveOutcome> Dismiss(Guid id, Guid adminId, string? note)
    {
        int updated = await connection.ExecuteAsync("""
            UPDATE content_report
            SET status = 'dismissed', resolved_by = @adminId, resolution_note = @note, resolved_at = now()
            WHERE id = @id AND status = 'open'
            """, new { id, adminId, note });
        if (updated > 0)
        {
            return ResolveOutcome.Resolved;
        }

        bool exists = await connection.QuerySingleAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM content_report WHERE id = @id)", new { id });
        return exists ? ResolveOutcome.AlreadyResolved : ResolveOutcome.NotFound;
    }

    /// <summary>Addresses of the Admins to tell about an urgent report.</summary>
    public async Task<List<string>> AdminEmails()
    {
        return (await connection.QueryAsync<string>("""
            SELECT email FROM app_user
            WHERE role = 'Admin' AND email IS NOT NULL AND deletion_requested_at IS NULL
            """)).ToList();
    }

    private sealed class LockedReport
    {
        public string Status { get; set; } = "";
        public Guid? ClipId { get; set; }
        public string Reason { get; set; } = "";
    }

    private sealed class TakenDownClip
    {
        public string? Title { get; set; }
        public string? Email { get; set; }
    }

    private async Task OpenAsync()
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }
    }
}
