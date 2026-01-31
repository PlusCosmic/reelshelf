namespace Nucleus.Shared.Auth;

/// <summary>
/// Represents a single entry in the whitelist configuration.
/// </summary>
public class WhitelistEntry
{
    public string DiscordId { get; set; } = string.Empty;
    public string? Role { get; set; }

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
