namespace Reelshelf.Users;

/// <summary>Sign-in providers an account can be linked to.</summary>
public static class AuthProvider
{
    public const string Discord = "discord";
    public const string Twitch = "twitch";

    public static readonly IReadOnlyList<string> All = [Discord, Twitch];

    public static bool IsKnown(string? provider)
    {
        return provider is not null && All.Contains(provider, StringComparer.Ordinal);
    }

    public static string? Normalize(string? provider)
    {
        string? lowered = provider?.Trim().ToLowerInvariant();
        return IsKnown(lowered) ? lowered : null;
    }
}

/// <summary>
/// Profile a provider reported for one of its users at sign-in time. Email is stored for the account owner
/// and never used to match sign-ins to accounts.
/// </summary>
public sealed record ExternalIdentity(
    string Provider,
    string ProviderUserId,
    string Username,
    string? DisplayName,
    string? AvatarUrl,
    string? Email);

/// <summary>Provider-level reference to an identity; enough to match whitelist entries.</summary>
public sealed record UserIdentityRef(string Provider, string ProviderUserId);

/// <summary>A linked identity as exposed to the account's owner.</summary>
public sealed record LinkedIdentity(
    string Provider,
    string ProviderUserId,
    string Username,
    string? DisplayName,
    string? AvatarUrl,
    string? Email,
    DateTimeOffset LinkedAt,
    bool IsPrimary);
