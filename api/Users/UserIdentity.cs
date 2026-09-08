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
    string? Email,
    ProviderTokens? Tokens = null);

/// <summary>
/// The provider's user access token as issued at sign-in, kept so the app can call the provider on the
/// user's behalf later (Twitch clip downloads). Null on identities whose provider we never call back.
/// </summary>
public sealed record ProviderTokens(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset? ExpiresAt,
    IReadOnlyList<string> Scopes)
{
    public bool HasScope(string scope) => Scopes.Contains(scope, StringComparer.Ordinal);

    /// <summary>True when the token is (or is about to be) expired and must be refreshed before use.</summary>
    public bool IsExpiring(DateTimeOffset now, TimeSpan leeway) =>
        ExpiresAt is { } expiresAt && expiresAt - leeway <= now;

    public static IReadOnlyList<string> ParseScopes(string? scopes) =>
        (scopes ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public string ScopesAsString() => string.Join(' ', Scopes);
}

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
