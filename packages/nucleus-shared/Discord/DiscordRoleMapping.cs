using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Nucleus.Shared.Auth;

namespace Nucleus.Shared.Discord;

/// <summary>
/// Manages the mapping between Discord role IDs and Nucleus UserRoles.
/// Configured via environment variables:
/// - DISCORD_ADMIN_ROLE_ID: Discord role ID that maps to Admin
/// - DISCORD_EDITOR_ROLE_ID: Discord role ID that maps to Editor
/// Users without a mapped role default to Viewer.
/// </summary>
public class DiscordRoleMapping
{
    private readonly ILogger<DiscordRoleMapping> _logger;
    private readonly Dictionary<ulong, UserRole> _roleMap = new();

    public DiscordRoleMapping(IConfiguration configuration, ILogger<DiscordRoleMapping> logger)
    {
        _logger = logger;

        // Load role mappings from configuration/environment variables
        var adminRoleId = configuration["DISCORD_ADMIN_ROLE_ID"] ?? configuration["DiscordAdminRoleId"];
        var editorRoleId = configuration["DISCORD_EDITOR_ROLE_ID"] ?? configuration["DiscordEditorRoleId"];

        if (!string.IsNullOrEmpty(adminRoleId) && ulong.TryParse(adminRoleId, out var adminId))
        {
            _roleMap[adminId] = UserRole.Admin;
            _logger.LogInformation("Mapped Discord role {RoleId} to Admin", adminId);
        }

        if (!string.IsNullOrEmpty(editorRoleId) && ulong.TryParse(editorRoleId, out var editorId))
        {
            _roleMap[editorId] = UserRole.Editor;
            _logger.LogInformation("Mapped Discord role {RoleId} to Editor", editorId);
        }

        if (_roleMap.Count == 0)
        {
            _logger.LogWarning("No Discord role mappings configured. " +
                "Set DISCORD_ADMIN_ROLE_ID and/or DISCORD_EDITOR_ROLE_ID environment variables.");
        }
    }

    /// <summary>
    /// Gets the Nucleus UserRole for a set of Discord role IDs.
    /// Returns the highest privilege role if multiple match.
    /// Returns null if no roles match (will use whitelist or default to Viewer).
    /// </summary>
    public UserRole? GetRoleForDiscordRoles(IEnumerable<ulong> discordRoleIds)
    {
        UserRole? highestRole = null;

        foreach (var roleId in discordRoleIds)
        {
            if (_roleMap.TryGetValue(roleId, out var mappedRole))
            {
                // Admin > Editor > Viewer
                if (highestRole is null || mappedRole > highestRole)
                {
                    highestRole = mappedRole;
                }
            }
        }

        return highestRole;
    }

    /// <summary>
    /// Checks if any role mappings are configured.
    /// </summary>
    public bool HasMappings => _roleMap.Count > 0;
}
