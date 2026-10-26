using Microsoft.AspNetCore.Http.HttpResults;
using Reelshelf.Auth;

namespace Reelshelf.Moderation;

/// <summary>Email is optional, so the reporter can hear the outcome or be asked for more.</summary>
public sealed record ContentReportRequest(string Reason, string? Details, string? Email);

/// <summary>A report as the admin queue shows it. <see cref="ShareUrl"/> is withheld for child-safety reports.</summary>
public sealed record AdminContentReport(
    Guid Id,
    string Reason,
    string Details,
    string? ReporterEmail,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt,
    string? ResolvedByName,
    string? ResolutionNote,
    Guid? ClipId,
    string? ClipTitle,
    Guid? OwnerId,
    string? OwnerName,
    string? ShareUrl,
    bool SharingBlocked,
    int OpenReportsOnClip);

public sealed record ResolveContentReportRequest(string? Note);

public static class ContentReportEndpoints
{
    private const int ListLimit = 100;

    public static void MapContentReportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("shared-clips/{token}/reports", ReportSharedClip)
            .WithName("ReportSharedClip")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.ContentReport);

        RouteGroupBuilder admin = app.MapGroup("admin/reports")
            .RequireAuthorization()
            .RequirePermission(Permissions.AdminReports);

        admin.MapGet("", ListReports).WithName("ListContentReports");
        admin.MapPost("{reportId:guid}/take-down", TakeDown).WithName("TakeDownContentReport");
        admin.MapPost("{reportId:guid}/dismiss", Dismiss).WithName("DismissContentReport");
    }

    private static async Task<Results<NoContent, NotFound>> ReportSharedClip(
        string token,
        ContentReportRequest request,
        ContentReportService reports)
    {
        bool filed = await reports.Report(token, request.Reason, request.Details, request.Email);
        return filed ? TypedResults.NoContent() : TypedResults.NotFound();
    }

    /// <summary>Open reports oldest first, or with <paramref name="resolved"/> the latest dealt with.</summary>
    private static async Task<Ok<List<AdminContentReport>>> ListReports(
        ContentReportStatements statements,
        bool? resolved)
    {
        List<ContentReportRow> rows = await statements.List(open: resolved != true, ListLimit);
        return TypedResults.Ok(rows.Select(ToReport).ToList());
    }

    private static async Task<Ok<AdminContentReport>> TakeDown(
        Guid reportId,
        ResolveContentReportRequest request,
        AuthenticatedUser admin,
        ContentReportService reports,
        ContentReportStatements statements)
    {
        await reports.TakeDown(admin, reportId, request.Note);
        return TypedResults.Ok(ToReport((await statements.Get(reportId))!));
    }

    private static async Task<Ok<AdminContentReport>> Dismiss(
        Guid reportId,
        ResolveContentReportRequest request,
        AuthenticatedUser admin,
        ContentReportService reports,
        ContentReportStatements statements)
    {
        await reports.Dismiss(admin, reportId, request.Note);
        return TypedResults.Ok(ToReport((await statements.Get(reportId))!));
    }

    private static AdminContentReport ToReport(ContentReportRow row)
    {
        // Admins are never handed a link to suspected child sexual abuse material: it is reported to the authorities,
        // not reviewed here.
        string? shareUrl = row.ActiveShareToken is { } token && row.Reason != ContentReportReasons.ChildSafety
            ? $"/share/{token}"
            : null;
        return new AdminContentReport(row.Id, row.Reason, row.Details, row.ReporterEmail, row.Status, row.CreatedAt,
            row.ResolvedAt, row.ResolvedByName, row.ResolutionNote, row.ClipId, row.ClipTitle, row.ClipOwnerId,
            row.OwnerName, shareUrl, row.SharingBlocked, row.OpenReportsOnClip);
    }
}
