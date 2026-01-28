using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Nucleus.Shared.Auth;

/// <summary>
/// Service that manages the whitelist configuration.
/// Registered as a singleton and loaded once at startup.
/// Provides O(1) lookups for Discord IDs and their associated roles.
/// </summary>
public class WhitelistService
{
    private readonly Dictionary<string, WhitelistEntry> _entries;
    private readonly ILogger<WhitelistService> _logger;

    public WhitelistService(ILogger<WhitelistService> logger)
    {
        _logger = logger;
        _entries = LoadWhitelist();
    }

    /// <summary>
    /// Checks if a Discord ID is in the whitelist.
    /// </summary>
    public bool IsWhitelisted(string discordId)
    {
        return _entries.ContainsKey(discordId);
    }

    /// <summary>
    /// Gets the whitelist entry for a Discord ID, or null if not found.
    /// </summary>
    public WhitelistEntry? GetEntry(string discordId)
    {
        return _entries.GetValueOrDefault(discordId);
    }

    /// <summary>
    /// Gets the explicit role for a Discord ID, or null if not explicitly set.
    /// </summary>
    public UserRole? GetRole(string discordId)
    {
        return _entries.TryGetValue(discordId, out var entry)
            ? entry.GetRole()
            : null;
    }

    /// <summary>
    /// Checks if a Discord ID has an explicit role override in the whitelist.
    /// </summary>
    public bool HasExplicitRole(string discordId)
    {
        return _entries.TryGetValue(discordId, out var entry) && entry.HasExplicitRole;
    }

    /// <summary>
    /// Gets the count of whitelisted users.
    /// </summary>
    public int Count => _entries.Count;

    private Dictionary<string, WhitelistEntry> LoadWhitelist()
    {
        string whitelistPath = Path.Combine(AppContext.BaseDirectory, "whitelist.json");

        if (!File.Exists(whitelistPath))
        {
            _logger.LogWarning("whitelist.json not found at {Path}, denying all requests", whitelistPath);
            return new Dictionary<string, WhitelistEntry>();
        }

        try
        {
            string json = File.ReadAllText(whitelistPath);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            // Try new format first
            var config = JsonSerializer.Deserialize<WhitelistConfig>(json, options);
            if (config?.Users is { Count: > 0 })
            {
                var entries = config.Users.ToDictionary(e => e.DiscordId, e => e);
                _logger.LogInformation("Loaded {Count} whitelisted users (new format)", entries.Count);
                return entries;
            }

            // Fall back to legacy format (flat array)
            var legacyConfig = JsonSerializer.Deserialize<LegacyWhitelistConfig>(json, options);
            if (legacyConfig?.WhitelistedDiscordUserIds is { Count: > 0 })
            {
                var entries = legacyConfig.WhitelistedDiscordUserIds
                    .ToDictionary(
                        id => id,
                        id => new WhitelistEntry { DiscordId = id, Role = nameof(UserRole.Viewer) });
                _logger.LogInformation("Loaded {Count} whitelisted users (legacy format)", entries.Count);
                return entries;
            }

            _logger.LogWarning("whitelist.json is empty or has invalid format");
            return new Dictionary<string, WhitelistEntry>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load whitelist.json");
            return new Dictionary<string, WhitelistEntry>();
        }
    }

    private class LegacyWhitelistConfig
    {
        public List<string>? WhitelistedDiscordUserIds { get; set; }
    }
}
