using Dapper;
using Npgsql;
using Reelshelf.Auth;

namespace Reelshelf.Moderation;

public static class ContentReportStatuses
{
    public const string Open = "open";
    public const string TakenDown = "taken_down";
    public const string Dismissed = "dismissed";

    /// <summary>The clip was removed and kept as evidence, and its owner suspended (<see cref="ContentReportStatements.Remove"/>).</summary>
    public const string Removed = "removed";
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

    public bool OwnerSuspended { get; set; }

    /// <summary>For a removed clip: its evidence's status, last error, purge result and deletion date.</summary>
    public string? EvidenceStatus { get; set; }
    public string? EvidenceError { get; set; }
    public string? EvidenceCdnPurge { get; set; }
    public DateTimeOffset? EvidenceDeleteAfter { get; set; }
}

public enum ResolveOutcome
{
    Resolved,
    NotFound,

    /// <summary>The report was already taken down or dismissed.</summary>
    AlreadyResolved,

    /// <summary>A child-safety or terrorism report: its clip is removed, not just taken down.</summary>
    NeedsRemoval
}

public enum RemoveOutcome
{
    Removed,

    /// <summary>The clip had already been deleted, so there was nothing to keep; the reports are resolved.</summary>
    ClipGone,
    NotFound,
    AlreadyResolved,

    /// <summary>Only child-safety and terrorism reports remove a clip; others are taken down.</summary>
    NotUrgent
}

/// <summary>A removal, and whether its owner could be suspended (an Admin can't be).</summary>
public sealed record RemoveResult(RemoveOutcome Outcome, Guid? ClipId, Guid? EvidenceId, bool OwnerSuspended);

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
    /// Whether a clip may be seen through a share link: it hasn't been taken down, isn't waiting on review of an
    /// <see cref="ContentReportReasons.Urgent"/> report, and its owner isn't suspended.
    /// </summary>
    public static string ShareableSql(string clipAlias) => $"""
        {clipAlias}.sharing_blocked_at IS NULL
        AND NOT EXISTS (
            SELECT 1 FROM app_user suspended_owner
            WHERE suspended_owner.id = {clipAlias}.owner_id AND suspended_owner.suspended_at IS NOT NULL)
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
        coalesce(owner.suspended_at IS NOT NULL, false) AS owner_suspended,
        evidence.status AS evidence_status, evidence.last_error AS evidence_error,
        evidence.cdn_purge AS evidence_cdn_purge, evidence.delete_after AS evidence_delete_after,
        (SELECT count(*)::int FROM content_report other
         WHERE other.clip_id = r.clip_id AND other.status = 'open') AS open_reports_on_clip
        FROM content_report r
        LEFT JOIN clip c ON c.id = r.clip_id
        LEFT JOIN app_user owner ON owner.id = r.clip_owner_id
        LEFT JOIN app_user resolver ON resolver.id = r.resolved_by
        LEFT JOIN clip_evidence evidence ON evidence.id = r.evidence_id
        """;

    /// <summary>
    /// Whether a child-safety or terrorism report on the clip, or on any of the owner's clips, is waiting for an admin.
    /// Such a clip is held as possible evidence: neither it nor its owner's account may be deleted until the report is
    /// dealt with.
    /// </summary>
    public async Task<bool> IsHeldForReview(Guid? clipId = null, Guid? ownerId = null)
    {
        return await connection.QuerySingleAsync<bool>($"""
            SELECT EXISTS (
                SELECT 1 FROM content_report r
                JOIN clip c ON c.id = r.clip_id
                WHERE r.status = 'open' AND r.reason IN ({ContentReportReasons.UrgentSqlList})
                  AND (r.clip_id = @clipId OR c.owner_id = @ownerId))
            """, new { clipId, ownerId });
    }

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

        if (ContentReportReasons.IsUrgent(found.Reason))
        {
            return new TakeDownResult(ResolveOutcome.NeedsRemoval, found.ClipId, null, null, found.Reason);
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

    /// <summary>
    /// Removes the clip of an open child-safety or terrorism report, in one transaction: snapshots the clip (with its
    /// game's name) and its owner (with every linked sign-in) into a new <c>clip_evidence</c> record, suspends the owner
    /// unless they are an Admin, resolves every open report on the clip as removed, and deletes the clip row, so nothing
    /// lists or plays it from then on. Its video is still at its host until <see cref="ClipEvidenceProcessor"/> has
    /// copied and deleted it. The owner isn't told.
    /// </summary>
    public async Task<RemoveResult> Remove(Guid id, Guid adminId, string note, TimeSpan retention)
    {
        await OpenAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();

        LockedReport? found = await connection.QuerySingleOrDefaultAsync<LockedReport>(
            "SELECT status, clip_id, reason FROM content_report WHERE id = @id FOR UPDATE",
            new { id }, transaction);
        if (found is null)
        {
            return new RemoveResult(RemoveOutcome.NotFound, null, null, false);
        }

        if (found.Status != ContentReportStatuses.Open)
        {
            return new RemoveResult(RemoveOutcome.AlreadyResolved, found.ClipId, null, false);
        }

        if (!ContentReportReasons.IsUrgent(found.Reason))
        {
            return new RemoveResult(RemoveOutcome.NotUrgent, found.ClipId, null, false);
        }

        Guid? ownerId = found.ClipId is { } clipId
            ? await connection.QuerySingleOrDefaultAsync<Guid?>(
                "SELECT owner_id FROM clip WHERE id = @clipId FOR UPDATE", new { clipId }, transaction)
            : null;
        if (ownerId is not { } owner)
        {
            await connection.ExecuteAsync("""
                UPDATE content_report
                SET status = 'removed', resolved_by = @adminId, resolution_note = @note, resolved_at = now()
                WHERE id = @id
                """, new { id, adminId, note }, transaction);
            await transaction.CommitAsync();
            return new RemoveResult(RemoveOutcome.ClipGone, found.ClipId, null, false);
        }

        Guid removedClipId = found.ClipId!.Value;
        Guid evidenceId = await connection.QuerySingleAsync<Guid>("""
            INSERT INTO clip_evidence (clip_id, owner_id, reason, removed_by, note, clip, owner, delete_after)
            SELECT c.id, c.owner_id, @reason, @adminId, @note,
                   to_jsonb(c) || jsonb_build_object('game_name', game.name),
                   jsonb_build_object(
                       'id', account.id,
                       'username', account.username,
                       'global_name', account.global_name,
                       'email', account.email,
                       'identities', coalesce((
                           SELECT jsonb_agg(jsonb_build_object(
                               'provider', identity.provider,
                               'provider_user_id', identity.provider_user_id,
                               'username', identity.username,
                               'display_name', identity.display_name,
                               'email', identity.email,
                               'linked_at', identity.linked_at) ORDER BY identity.linked_at)
                           FROM user_identity identity WHERE identity.user_id = account.id), '[]'::jsonb)),
                   now() + @retention
            FROM clip c
            JOIN app_user account ON account.id = c.owner_id
            LEFT JOIN game_category game ON game.id = c.game_category_id
            WHERE c.id = @clipId
            RETURNING id
            """, new { clipId = removedClipId, reason = found.Reason, adminId, note, retention }, transaction);

        AccessChangeOutcome suspension = await AccountAccessStatements.SetSuspended(connection, transaction, owner,
            true, adminId, $"Clip {removedClipId} removed after a {ContentReportReasons.Describe(found.Reason)} report");

        await connection.ExecuteAsync("""
            UPDATE content_report
            SET status = 'removed', evidence_id = @evidenceId, resolved_by = @adminId, resolution_note = @note,
                resolved_at = now()
            WHERE status = 'open' AND (id = @id OR clip_id = @clipId)
            """, new { id, clipId = removedClipId, evidenceId, adminId, note }, transaction);
        await connection.ExecuteAsync("DELETE FROM clip WHERE id = @clipId", new { clipId = removedClipId },
            transaction);
        await transaction.CommitAsync();

        return new RemoveResult(RemoveOutcome.Removed, removedClipId, evidenceId,
            suspension is AccessChangeOutcome.Changed or AccessChangeOutcome.Unchanged);
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
