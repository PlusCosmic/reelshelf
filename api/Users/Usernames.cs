using System.Globalization;
using System.Text;

namespace Reelshelf.Users;

/// <summary>
/// Usernames an account owner picks for themselves (when their primary sign-in is Google, which has none).
/// They follow Discord's shape, so they sit alongside provider usernames: 2 to 32 lower-case letters, digits,
/// underscores and full stops. People add collaborators by username, so a chosen one may not match another
/// account's, ignoring case.
/// </summary>
public static class Usernames
{
    public const int MinLength = 2;
    public const int MaxLength = 32;

    /// <summary>Stands in until the owner picks one when nothing usable can be made from their name.</summary>
    public const string Fallback = "player";

    /// <summary>Trims and lower-cases <paramref name="input"/>; false when the result isn't a valid username.</summary>
    public static bool TryNormalize(string? input, out string username)
    {
        username = (input ?? "").Trim().ToLowerInvariant();
        return username.Length is >= MinLength and <= MaxLength && username.All(IsAllowed);
    }

    /// <summary>
    /// A starting username made from a display name ("Harry Smith" becomes "harrysmith"), offered to the owner
    /// to keep or change. Accents are dropped and anything else outside the allowed characters removed.
    /// </summary>
    public static string Suggest(string? displayName)
    {
        StringBuilder builder = new();
        foreach (char c in (displayName ?? "").Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            char lower = char.ToLowerInvariant(c);
            if (IsAllowed(lower))
            {
                builder.Append(lower);
            }
        }

        string suggestion = builder.ToString().Trim('.', '_');
        if (suggestion.Length > MaxLength)
        {
            suggestion = suggestion[..MaxLength].TrimEnd('.', '_');
        }

        return suggestion.Length >= MinLength ? suggestion : Fallback;
    }

    private static bool IsAllowed(char c)
    {
        return c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '.';
    }
}
