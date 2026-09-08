using System.Net;
using Reelshelf.Storage;

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

    public static EmailMessage StorageNearlyFull(string to, StorageQuota quota, string libraryUrl)
    {
        string used = StorageQuotaService.FormatBytes(quota.UsedBytes);
        string limit = StorageQuotaService.FormatBytes(quota.LimitBytes ?? 0);
        string remaining = StorageQuotaService.FormatBytes(quota.RemainingBytes ?? 0);
        string subject = "Your Reelshelf storage is nearly full";
        string text =
            $"You've used {used} of your {limit} of clip storage; {remaining} remain.\n\n" +
            $"Once it's full, new uploads are refused until you delete some clips: {libraryUrl}\n";

        return new EmailMessage(to, subject, text, Wrap(subject,
            $"<p>You've used <strong>{Encode(used)}</strong> of your {Encode(limit)} of clip storage; {Encode(remaining)} remain.</p>" +
            $"<p>Once it's full, new uploads are refused until you <a href=\"{Encode(libraryUrl)}\">delete some clips</a>.</p>"));
    }

    private static string Wrap(string title, string body)
    {
        return "<!doctype html><html><body style=\"font-family:system-ui,sans-serif;line-height:1.5;color:#222\">" +
               $"<h2 style=\"font-weight:600\">{Encode(title)}</h2>{body}" +
               "<p style=\"color:#777;font-size:12px\">Sent by Reelshelf. Change or remove your address in Settings.</p>" +
               "</body></html>";
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
