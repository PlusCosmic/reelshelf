using System.Net;
using Reelshelf.Plans;

namespace Reelshelf.Email;

/// <summary>The account notices Reelshelf sends. Plain text first; the HTML is the same content lightly wrapped.</summary>
public static class AccountEmails
{
    public static EmailMessage IdentityLinked(string to, string providerLabel, string providerUsername, string settingsUrl)
    {
        string subject = $"{providerLabel} was linked to your Reelshelf account";
        string text =
            $"A {providerLabel} login (@{providerUsername}) can now sign in to your Reelshelf account.\n\n" +
            $"If this was you, there's nothing to do. If it wasn't, unlink it now: {settingsUrl}\n";

        return new EmailMessage(to, subject, text, Wrap(subject,
            $"<p>A {Encode(providerLabel)} login (<strong>@{Encode(providerUsername)}</strong>) can now sign in to your Reelshelf account.</p>" +
            $"<p>If this was you, there's nothing to do. If it wasn't, <a href=\"{Encode(settingsUrl)}\">unlink it now</a>.</p>"));
    }

    public static EmailMessage ClipLimitNearlyReached(string to, AccountPlan plan, string libraryUrl)
    {
        int limit = plan.Limits.MaxClips ?? 0;
        string kept = $"{plan.ClipCount:N0} of the {limit:N0} clips";
        int remaining = Math.Max(0, limit - plan.ClipCount);
        string subject = "Your Reelshelf library is nearly full";
        string text =
            $"You're keeping {kept} your plan allows; {remaining:N0} more will fit.\n\n" +
            $"Once it's full, new uploads are refused until you delete some clips or upgrade: {libraryUrl}\n";

        return new EmailMessage(to, subject, text, Wrap(subject,
            $"<p>You're keeping <strong>{Encode(kept)}</strong> your plan allows; {remaining:N0} more will fit.</p>" +
            $"<p>Once it's full, new uploads are refused until you <a href=\"{Encode(libraryUrl)}\">delete some clips</a> or upgrade.</p>"));
    }

    /// <summary>The account went over its plan's limit: the clips over it go after the grace.</summary>
    public static EmailMessage OverLimitStarted(string to, int overCount, DateTimeOffset deletesAt, string planUrl)
    {
        string clips = Clips(overCount);
        string date = Date(deletesAt);
        string subject = "Some of your Reelshelf clips are over your plan's limit";
        string text =
            $"Your plan now keeps fewer clips than you have, so {clips} {Are(overCount)} over its limit. They'll be deleted on {date}.\n\n" +
            $"Before then you can download them, pick which clips to keep, or upgrade to keep them all: {planUrl}\n";

        return new EmailMessage(to, subject, text, Wrap(subject,
            $"<p>Your plan now keeps fewer clips than you have, so <strong>{Encode(clips)}</strong> {Are(overCount)} over its limit. " +
            $"They'll be deleted on <strong>{Encode(date)}</strong>.</p>" +
            $"<p>Before then you can <a href=\"{Encode(planUrl)}\">download them, pick which clips to keep, or upgrade</a> to keep them all.</p>"));
    }

    /// <summary>A few days of the grace are left.</summary>
    public static EmailMessage OverLimitEndingSoon(string to, int overCount, DateTimeOffset deletesAt, string planUrl)
    {
        string clips = Clips(overCount);
        string date = Date(deletesAt);
        string subject = $"{char.ToUpperInvariant(clips[0])}{clips[1..]} will be deleted on {date}";
        string text =
            $"{clips} {Are(overCount)} still over your plan's limit and will be deleted on {date}. We'll email you a download of them " +
            $"then, kept for 28 days.\n\nTo keep them, upgrade or download them now: {planUrl}\n";

        return new EmailMessage(to, subject, text, Wrap(subject,
            $"<p>{Encode(clips)} {Are(overCount)} still over your plan's limit and will be deleted on <strong>{Encode(date)}</strong>. " +
            "We'll email you a download of them then, kept for 28 days.</p>" +
            $"<p>To keep them, <a href=\"{Encode(planUrl)}\">upgrade or download them now</a>.</p>"));
    }

    /// <summary>An export's zips are ready; for a grace that ended, its clips have been deleted.</summary>
    public static EmailMessage ExportReady(string to, int clipCount, bool clipsDeleted, DateTimeOffset expiresAt,
        string planUrl)
    {
        string clips = Clips(clipCount);
        string date = Date(expiresAt);
        string subject = clipsDeleted ? "Your deleted Reelshelf clips are ready to download" : "Your Reelshelf download is ready";
        string lead = clipsDeleted
            ? $"{clips} over your plan's limit {(clipCount == 1 ? "has" : "have")} been deleted from Reelshelf. A copy of them is ready to download."
            : $"The download of {clips} you asked for is ready.";
        string text = $"{lead}\n\nDownload it by {date}, when it's deleted: {planUrl}\n";

        return new EmailMessage(to, subject, text, Wrap(subject,
            $"<p>{Encode(lead)}</p><p><a href=\"{Encode(planUrl)}\">Download it</a> by <strong>{Encode(date)}</strong>, when it's deleted.</p>"));
    }

    /// <summary>A paid plan was set to end, and the account has more clips than it keeps afterwards.</summary>
    public static EmailMessage PlanEnding(string to, string tierName, DateTimeOffset endsAt, int overCount,
        TimeSpan gracePeriod, string planUrl)
    {
        string clips = Clips(overCount);
        string date = Date(endsAt);
        string subject = $"Your Reelshelf {tierName} plan ends on {date}";
        string text =
            $"Your {tierName} plan ends on {date}. After that your account keeps fewer clips, so {clips} will be over the " +
            $"limit; they're kept for {gracePeriod.TotalDays:0} more days, then deleted.\n\n" +
            $"Download them, pick which to keep, or keep your plan: {planUrl}\n";

        return new EmailMessage(to, subject, text, Wrap(subject,
            $"<p>Your {Encode(tierName)} plan ends on <strong>{Encode(date)}</strong>. After that your account keeps fewer clips, " +
            $"so {Encode(clips)} will be over the limit; they're kept for {gracePeriod.TotalDays:0} more days, then deleted.</p>" +
            $"<p><a href=\"{Encode(planUrl)}\">Download them, pick which to keep, or keep your plan</a>.</p>"));
    }

    private static string Clips(int count) => count == 1 ? "1 clip" : $"{count:N0} clips";

    private static string Are(int count) => count == 1 ? "is" : "are";

    private static string Date(DateTimeOffset value) =>
        value.UtcDateTime.ToString("d MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("en-GB"));

    public static EmailMessage AccountDeletionStarted(string to)
    {
        string subject = "Your Reelshelf account is being deleted";
        string text =
            "Your Reelshelf account and everything in it, including your clips, collections and share links, " +
            "are being permanently deleted. This can't be undone.\n\n" +
            "If you didn't ask for this, someone may have access to your Discord or Twitch account; secure it now.\n";

        return new EmailMessage(to, subject, text, Wrap(subject,
            "<p>Your Reelshelf account and everything in it, including your clips, collections and share links, " +
            "are being permanently deleted. This can't be undone.</p>" +
            "<p>If you didn't ask for this, someone may have access to your Discord or Twitch account; secure it now.</p>",
            footer: "Sent by Reelshelf."));
    }

    /// <summary>To an Admin: a shared clip was reported for something serious, and its link is hidden until reviewed.</summary>
    public static EmailMessage UrgentContentReport(string to, string reason, string reviewUrl)
    {
        string subject = "A shared clip was reported and needs review";
        string text =
            $"A shared clip was reported for {reason}. Its share link is hidden until you review it: {reviewUrl}\n";

        return new EmailMessage(to, subject, text, Wrap(subject,
            $"<p>A shared clip was reported for <strong>{Encode(reason)}</strong>. Its share link is hidden until you " +
            $"<a href=\"{Encode(reviewUrl)}\">review it</a>.</p>",
            footer: "Sent by Reelshelf to its admins."));
    }

    /// <summary>
    /// To the owner: a clip was taken down for breaking the terms, and how to dispute it. It names neither the rule nor
    /// the report, so nothing in it points back at whoever reported the clip.
    /// </summary>
    public static EmailMessage ClipTakenDown(string to, string clipTitle, string termsUrl, string contactEmail)
    {
        string subject = "One of your Reelshelf clips can no longer be shared";
        string text =
            $"Your clip \"{clipTitle}\" breaks our terms of service, so its share links have stopped working and it " +
            "can't be shared again. It is still in your library.\n\n" +
            $"Our terms: {termsUrl}\n" +
            $"If you think we got this wrong, email {contactEmail} and tell us why.\n";

        return new EmailMessage(to, subject, text, Wrap(subject,
            $"<p>Your clip <strong>{Encode(clipTitle)}</strong> breaks our <a href=\"{Encode(termsUrl)}\">terms of " +
            "service</a>, so its share links have stopped working and it can't be shared again. It is still in your " +
            "library.</p>" +
            $"<p>If you think we got this wrong, email <a href=\"mailto:{Encode(contactEmail)}\">{Encode(contactEmail)}</a> " +
            "and tell us why.</p>",
            footer: "Sent by Reelshelf."));
    }

    private static string Wrap(string title, string body,
        string footer = "Sent by Reelshelf. Change or remove your address in Settings.")
    {
        return "<!doctype html><html><body style=\"font-family:system-ui,sans-serif;line-height:1.5;color:#222\">" +
               $"<h2 style=\"font-weight:600\">{Encode(title)}</h2>{body}" +
               $"<p style=\"color:#777;font-size:12px\">{Encode(footer)}</p>" +
               "</body></html>";
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
