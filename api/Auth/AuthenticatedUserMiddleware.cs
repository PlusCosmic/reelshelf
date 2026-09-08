using System.Security.Claims;
using Reelshelf.Discord;

namespace Reelshelf.Auth;

/// <summary>
/// Middleware that resolves the authenticated user from the database and stores it in HttpContext.Items.
/// This middleware runs after authentication and before endpoint execution.
/// The user is then available via AuthenticatedUser.BindAsync for parameter binding.
/// Also syncs roles from whitelist.json to the database (whitelist is source of truth for base roles).
/// </summary>
public class AuthenticatedUserMiddleware(RequestDelegate next, WhitelistService whitelistService)
{
    private readonly HashSet<string> _bypassPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/health",
        "/auth",
        "/webhooks"
    };

    /// <summary>
    /// Configures additional bypass paths for user resolution.
    /// </summary>
    public void AddBypassPaths(params string[] paths)
    {
        foreach (string path in paths)
        {
            _bypassPaths.Add(path);
        }
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip user resolution for bypass paths
        string path = context.Request.Path.Value ?? "";
        if (_bypassPaths.Any(bp => path.StartsWith(bp, StringComparison.OrdinalIgnoreCase)))
        {
            await next(context);
            return;
        }

        // Skip if not authenticated
        string? discordId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(discordId))
        {
            await next(context);
            return;
        }

        // Resolve the user from the database and cache in HttpContext.Items
        DiscordStatements discordStatements = context.RequestServices.GetRequiredService<DiscordStatements>();
        DiscordStatements.DiscordUserRow? dbUser = await discordStatements.GetUserByDiscordId(discordId);

        if (dbUser is not null)
        {
            // Determine the effective role:
            // 1. If whitelist.json pins a role, use it and persist it (flagged as whitelist-driven).
            // 2. If the stored role was pinned by the whitelist but the entry is gone, revoke it: fall back
            //    to the default role so removing an override actually removes the privileges.
            // 3. Otherwise use the stored role (default, or set by some other mechanism).
            UserRole? whitelistRole = whitelistService.GetRole(discordId);
            UserRole dbRole = ParseRole(dbUser.Role);
            UserRole effectiveRole;

            if (whitelistRole is { } pinnedRole)
            {
                effectiveRole = pinnedRole;
                if (dbRole != pinnedRole || !dbUser.RoleFromWhitelist)
                {
                    await discordStatements.UpdateUserRole(dbUser.Id, pinnedRole.ToString(), roleFromWhitelist: true);
                }
            }
            else if (dbUser.RoleFromWhitelist)
            {
                effectiveRole = DefaultRole;
                await discordStatements.UpdateUserRole(dbUser.Id, DefaultRole.ToString(), roleFromWhitelist: false);
            }
            else
            {
                effectiveRole = dbRole;
            }

            // Load additional permissions
            List<string> additionalPermissions = await discordStatements.GetUserAdditionalPermissions(dbUser.Id);

            context.Items[AuthenticatedUser.HttpContextKey] = new AuthenticatedUser(
                dbUser.Id,
                dbUser.DiscordId,
                dbUser.Username,
                dbUser.GlobalName,
                dbUser.Avatar,
                effectiveRole,
                new HashSet<string>(additionalPermissions));
        }

        await next(context);
    }

    /// <summary>Role for accounts with no whitelist override; matches the discord_user.role column default.</summary>
    private const UserRole DefaultRole = UserRole.Editor;

    /// <summary>
    /// New accounts get <see cref="DefaultRole"/> from the column default, so the parser never needs to grant
    /// privileges: an unrecognised stored value falls back to Viewer rather than Editor.
    /// </summary>
    private static UserRole ParseRole(string roleString)
    {
        return Enum.TryParse<UserRole>(roleString, ignoreCase: true, out UserRole role)
            ? role
            : UserRole.Viewer;
    }
}

/// <summary>
/// Extension methods for registering AuthenticatedUserMiddleware.
/// </summary>
public static class AuthenticatedUserMiddlewareExtensions
{
    public static IApplicationBuilder UseAuthenticatedUserResolution(this IApplicationBuilder app)
    {
        return app.UseMiddleware<AuthenticatedUserMiddleware>();
    }
}
