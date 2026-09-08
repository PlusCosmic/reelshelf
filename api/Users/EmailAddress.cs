using System.Net.Mail;

namespace Reelshelf.Users;

public static class EmailAddress
{
    public const int MaxLength = 254;

    /// <summary>
    /// Trims and lower-cases an address, returning null for blank input and false for anything that is not a
    /// plain single mailbox (no display names, no angle brackets).
    /// </summary>
    public static bool TryNormalize(string? input, out string? email)
    {
        email = null;
        string trimmed = input?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            return true;
        }

        if (trimmed.Length > MaxLength ||
            !MailAddress.TryCreate(trimmed, out MailAddress? parsed) ||
            !string.Equals(parsed.Address, trimmed, StringComparison.OrdinalIgnoreCase) ||
            !parsed.Host.Contains('.'))
        {
            return false;
        }

        email = trimmed.ToLowerInvariant();
        return true;
    }
}
