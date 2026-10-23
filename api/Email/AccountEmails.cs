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
