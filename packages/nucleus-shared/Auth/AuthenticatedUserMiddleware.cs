using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nucleus.Shared.Discord;

namespace Nucleus.Shared.Auth;

/// <summary>
/// Middleware that resolves the authenticated user from the database and stores it in HttpContext.Items.
/// This middleware runs after WhitelistMiddleware and before endpoint execution.
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
            // 1. If whitelist has explicit role override, use it
            // 2. Otherwise, use database role (set by Discord sync or default)
            UserRole? whitelistRole = whitelistService.GetRole(discordId);
            UserRole dbRole = ParseRole(dbUser.Role);
            UserRole effectiveRole = whitelistRole ?? dbRole;

            // Sync whitelist role to database if explicitly set and different
            if (whitelistRole.HasValue && dbRole != whitelistRole.Value)
            {
                await discordStatements.UpdateUserRole(dbUser.Id, whitelistRole.Value.ToString());
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

    private static UserRole ParseRole(string roleString)
    {
        return Enum.TryParse<UserRole>(roleString, ignoreCase: true, out UserRole role)
            ? role
            : UserRole.Viewer; // Default to Viewer if parsing fails
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
