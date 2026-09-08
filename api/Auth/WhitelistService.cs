using System.Text.Json;
using Reelshelf.Users;

namespace Reelshelf.Auth;

/// <summary>
/// Service that manages the whitelist configuration.
/// Registered as a singleton and loaded once at startup.
/// Provides O(1) lookups by linked identity (provider + provider user id).
///
/// The whitelist does not gate access: any Discord or Twitch account can sign in and use the default storage tier.
/// Listed users get unlimited storage, and an entry may also pin a role override.
/// </summary>
public class WhitelistService
{
    private readonly Dictionary<UserIdentityRef, WhitelistEntry> _entries;

    public WhitelistService(ILogger<WhitelistService> logger)
    {
        _entries = LoadWhitelist(logger);
    }

    internal WhitelistService(Dictionary<UserIdentityRef, WhitelistEntry> entries)
    {
        _entries = entries;
    }

    /// <summary>
    /// Checks if any of the identities is in the whitelist (unlimited storage tier).
    /// </summary>
    public bool IsWhitelisted(IEnumerable<UserIdentityRef> identities)
    {
        return GetEntry(identities) is not null;
    }

    /// <summary>
    /// Gets the whitelist entry matching any of the identities, or null if none match.
    /// When several match, the entry with an explicit role wins so a role override is never lost.
    /// </summary>
    public WhitelistEntry? GetEntry(IEnumerable<UserIdentityRef> identities)
    {
        WhitelistEntry? match = null;
        foreach (UserIdentityRef identity in identities)
        {
            if (!_entries.TryGetValue(identity, out WhitelistEntry? entry))
            {
                continue;
            }

            if (entry.HasExplicitRole)
            {
                return entry;
            }

            match ??= entry;
        }

        return match;
    }

    /// <summary>
    /// Gets the explicit role pinned for any of the identities, or null if none is set.
    /// </summary>
    public UserRole? GetRole(IEnumerable<UserIdentityRef> identities)
    {
        return GetEntry(identities)?.GetRole();
    }

    /// <summary>
    /// Gets the count of whitelisted identities.
    /// </summary>
    public int Count => _entries.Count;

    private static Dictionary<UserIdentityRef, WhitelistEntry> LoadWhitelist(ILogger logger)
    {
        string whitelistPath = Path.Combine(AppContext.BaseDirectory, "whitelist.json");

        if (!File.Exists(whitelistPath))
        {
            logger.LogWarning("whitelist.json not found at {Path}; every user gets the default storage tier", whitelistPath);
            return new Dictionary<UserIdentityRef, WhitelistEntry>();
        }

        try
        {
            return Parse(File.ReadAllText(whitelistPath), logger);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load whitelist.json");
            return new Dictionary<UserIdentityRef, WhitelistEntry>();
        }
    }

    internal static Dictionary<UserIdentityRef, WhitelistEntry> Parse(string json, ILogger logger)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        // Try new format first
        var config = JsonSerializer.Deserialize<WhitelistConfig>(json, options);
        if (config?.Users is { Count: > 0 })
        {
            var entries = new Dictionary<UserIdentityRef, WhitelistEntry>();
            foreach (WhitelistEntry entry in config.Users)
            {
                foreach (UserIdentityRef identity in entry.Identities())
                {
                    entries[identity] = entry;
                }
            }

            logger.LogInformation("Loaded {Count} whitelisted identities (new format)", entries.Count);
            return entries;
        }

        // Fall back to legacy format (flat array)
        var legacyConfig = JsonSerializer.Deserialize<LegacyWhitelistConfig>(json, options);
        if (legacyConfig?.WhitelistedDiscordUserIds is { Count: > 0 })
        {
            // Legacy entries grant the unlimited tier only; they must not pin a role,
            // or the role sync would demote these users back to Viewer (who cannot upload).
            var entries = legacyConfig.WhitelistedDiscordUserIds
                .ToDictionary(
                    id => new UserIdentityRef(AuthProvider.Discord, id),
                    id => new WhitelistEntry { DiscordId = id });
            logger.LogInformation("Loaded {Count} whitelisted users (legacy format)", entries.Count);
            return entries;
        }

        logger.LogWarning("whitelist.json is empty or has invalid format");
        return new Dictionary<UserIdentityRef, WhitelistEntry>();
    }

    private class LegacyWhitelistConfig
    {
        public List<string>? WhitelistedDiscordUserIds { get; set; }
    }
}
