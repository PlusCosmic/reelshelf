using Reelshelf.Auth;
using Reelshelf.Email;
using Reelshelf.Exceptions;
using Reelshelf.Users;

namespace Reelshelf.Moderation;

/// <summary>
/// Files reports of shared clips and resolves them. An <see cref="ContentReportReasons.Urgent"/> report hides the share
/// link at once and mails the Admins; a takedown mails the clip's owner what was removed and why.
/// </summary>
public sealed class ContentReportService(
    ContentReportStatements statements,
    IEmailSender emailSender,
    IConfiguration configuration,
    ILogger<ContentReportService> logger)
{
    public const int MaxDetailsLength = 2000;
    public const int MaxNoteLength = 500;
    public const string DefaultContactEmail = "support@reelshelf.app";

    private readonly string _siteUrl = (configuration["FrontendOrigin"] ?? "http://localhost:5173").TrimEnd('/');

    private readonly string _contactEmail =
        configuration["Moderation:ContactEmail"] is { Length: > 0 } contact ? contact : DefaultContactEmail;

    /// <summary>Files a report against the clip a share link shows; false when the link shows nothing.</summary>
    public async Task<bool> Report(string token, string? reason, string? details, string? email)
    {
        if (!ContentReportReasons.IsKnown(reason))
        {
            throw new BadRequestException("Pick a reason for the report.");
        }

        string trimmedDetails = details?.Trim() ?? "";
        if (trimmedDetails.Length > MaxDetailsLength)
        {
            throw new BadRequestException($"Keep the details under {MaxDetailsLength:N0} characters.");
        }

        if (!EmailAddress.TryNormalize(email, out string? reporterEmail))
        {
            throw new BadRequestException("That email address doesn't look right.");
        }

        ReportedShare? share = await statements.FindShare(token);
        if (share is null)
        {
            return false;
        }

        Guid reportId = await statements.Insert(new NewContentReport(share.ClipId, share.OwnerId, share.Title, token,
            reason!, trimmedDetails, reporterEmail));

        if (ContentReportReasons.IsUrgent(reason!))
        {
            logger.LogWarning("Clip {ClipId} reported for {Reason} (report {ReportId}); its share link is hidden until reviewed",
                share.ClipId, reason, reportId);
            string reviewUrl = $"{_siteUrl}/admin/reports";
            foreach (string admin in await statements.AdminEmails())
            {
                await emailSender.SendAsync(AccountEmails.UrgentContentReport(admin,
                    ContentReportReasons.Describe(reason!), reviewUrl));
            }
        }
        else
        {
            logger.LogInformation("Clip {ClipId} reported for {Reason} (report {ReportId})", share.ClipId, reason,
                reportId);
        }

        return true;
    }

    public async Task TakeDown(AuthenticatedUser admin, Guid reportId, string? note)
    {
        string trimmed = RequireNote(note);
        TakeDownResult result = await statements.TakeDown(reportId, admin.Id, trimmed);
        Check(result.Outcome, reportId);

        logger.LogWarning("Admin {AdminId} took down clip {ClipId} on report {ReportId}", admin.Id, result.ClipId,
            reportId);
        if (result.OwnerEmail is { } ownerEmail)
        {
            await emailSender.SendAsync(AccountEmails.ClipTakenDown(ownerEmail,
                string.IsNullOrWhiteSpace(result.ClipTitle) ? "Untitled clip" : result.ClipTitle,
                ContentReportReasons.Describe(result.Reason), _contactEmail));
        }
    }

    public async Task Dismiss(AuthenticatedUser admin, Guid reportId, string? note)
    {
        string? trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmed?.Length > MaxNoteLength)
        {
            throw new BadRequestException($"Keep the note under {MaxNoteLength} characters.");
        }

        Check(await statements.Dismiss(reportId, admin.Id, trimmed), reportId);
        logger.LogInformation("Admin {AdminId} dismissed report {ReportId}", admin.Id, reportId);
    }

    private static string RequireNote(string? note)
    {
        string trimmed = note?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            throw new BadRequestException("Say why, for the record.");
        }

        if (trimmed.Length > MaxNoteLength)
        {
            throw new BadRequestException($"Keep the note under {MaxNoteLength} characters.");
        }

        return trimmed;
    }

    private static void Check(ResolveOutcome outcome, Guid reportId)
    {
        switch (outcome)
        {
            case ResolveOutcome.NotFound:
                throw new NotFoundException("Report", reportId);
            case ResolveOutcome.AlreadyResolved:
                throw new ConflictException("That report has already been dealt with.");
        }
    }
}
