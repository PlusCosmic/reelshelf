using Microsoft.AspNetCore.DataProtection;

namespace Reelshelf.Users;

/// <summary>
/// Encrypts provider access and refresh tokens before they reach the database. The keys are the app's
/// data-protection ring, so a database dump alone does not yield usable Twitch credentials.
/// </summary>
public sealed class ProviderTokenProtector(IDataProtectionProvider dataProtectionProvider)
{
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector("Reelshelf.ProviderTokens.v1");

    public string? Protect(string? plaintext) =>
        string.IsNullOrEmpty(plaintext) ? null : _protector.Protect(plaintext);

    /// <summary>Returns null for ciphertext this key ring cannot open, so a rotated ring reads as "needs re-authorization".</summary>
    public string? Unprotect(string? ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext))
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(ciphertext);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }
}
