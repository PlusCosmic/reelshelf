using System.Security.Cryptography;
using System.Text;

namespace Reelshelf.Devices;

/// <summary>
/// The secrets of device sign-in (ADR-0010): the <c>device_code</c> the app polls with, the <c>user_code</c> its owner
/// types or follows on <c>/device</c>, and the token the app keeps. Only SHA-256 hashes of the device code and the
/// token are stored.
/// </summary>
public static class DeviceCodes
{
    /// <summary>Letters without lookalikes or vowels, so a code never spells a word or reads as a digit.</summary>
    public const string UserCodeAlphabet = "BCDFGHJKLMNPQRSTVWXZ";

    public const int UserCodeLength = 8;

    /// <summary>Lets secret scanners recognise a leaked token.</summary>
    public const string TokenPrefix = "rsd_";

    private const int SecretBytes = 32;

    public static string NewDeviceCode() => Base64Url(RandomNumberGenerator.GetBytes(SecretBytes));

    public static string NewToken() => TokenPrefix + Base64Url(RandomNumberGenerator.GetBytes(SecretBytes));

    /// <summary>A new user code as stored: <see cref="UserCodeLength"/> letters, no dash.</summary>
    public static string NewUserCode()
    {
        Span<char> code = stackalloc char[UserCodeLength];
        for (int i = 0; i < code.Length; i++)
        {
            code[i] = UserCodeAlphabet[RandomNumberGenerator.GetInt32(UserCodeAlphabet.Length)];
        }

        return new string(code);
    }

    /// <summary>A stored user code as people see it, <c>XXXX-XXXX</c>.</summary>
    public static string FormatUserCode(string code) =>
        code.Length == UserCodeLength ? $"{code[..4]}-{code[4..]}" : code;

    /// <summary>
    /// A user code as typed, matched ignoring case, spaces and the dash, or null when it can't be one, so a lookup
    /// never runs for something that couldn't match.
    /// </summary>
    public static string? NormalizeUserCode(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed))
        {
            return null;
        }

        StringBuilder code = new(UserCodeLength);
        foreach (char c in typed)
        {
            if (c is '-' or ' ')
            {
                continue;
            }

            char upper = char.ToUpperInvariant(c);
            if (!UserCodeAlphabet.Contains(upper) || code.Length == UserCodeLength)
            {
                return null;
            }

            code.Append(upper);
        }

        return code.Length == UserCodeLength ? code.ToString() : null;
    }

    public static byte[] Hash(string secret) => SHA256.HashData(Encoding.UTF8.GetBytes(secret));

    /// <summary>Whether a presented token is shaped like one, before it is hashed and looked up.</summary>
    public static bool LooksLikeToken(string? token) =>
        token is { Length: > 4 } && token.StartsWith(TokenPrefix, StringComparison.Ordinal);

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
