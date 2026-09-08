using Reelshelf.Users;

namespace Reelshelf.Auth;

/// <summary>
/// Represents a single entry in the whitelist configuration.
/// Listed users get unlimited clip storage and may carry an explicit role override.
/// An entry names the person by one or more provider ids; any linked identity that matches applies the entry.
/// </summary>
public class WhitelistEntry
{
    public string? DiscordId { get; set; }
    public string? TwitchId { get; set; }
    public string? Role { get; set; }

    /// <summary>Provider identities this entry applies to.</summary>
    public IEnumerable<UserIdentityRef> Identities()
    {
        if (!string.IsNullOrWhiteSpace(DiscordId))
        {
            yield return new UserIdentityRef(AuthProvider.Discord, DiscordId.Trim());
        }

        if (!string.IsNullOrWhiteSpace(TwitchId))
        {
            yield return new UserIdentityRef(AuthProvider.Twitch, TwitchId.Trim());
        }
    }

    /// <summary>
    /// Gets the role, or null if not explicitly set.
    /// </summary>
    public UserRole? GetRole()
    {
        if (string.IsNullOrEmpty(Role))
            return null;

        return Enum.TryParse<UserRole>(Role, ignoreCase: true, out var role)
            ? role
            : null;
    }

    /// <summary>
    /// Returns true if this entry has an explicit role override.
    /// </summary>
    public bool HasExplicitRole => !string.IsNullOrEmpty(Role);
}

/// <summary>
/// Configuration model for whitelist.json.
/// Supports both the new format (Users array with roles) and legacy format (flat array).
/// </summary>
public class WhitelistConfig
{
    public List<WhitelistEntry> Users { get; set; } = [];
}
