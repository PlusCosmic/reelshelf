using System.Security.Claims;
using Reelshelf.Users;

namespace Reelshelf.Auth;

/// <summary>
/// Middleware that resolves the authenticated user from the database and stores it in HttpContext.Items.
/// This middleware runs after authentication and before endpoint execution.
/// The user is then available via AuthenticatedUser.BindAsync for parameter binding.
/// The account's role and granted tier are read as stored (ADR-0008); only <see cref="AccountAccessService"/>
/// changes them.
/// </summary>
public class AuthenticatedUserMiddleware(RequestDelegate next)
{
    private static readonly string[] BypassPaths = ["/health", "/auth"];

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip user resolution for bypass paths
        string path = context.Request.Path.Value ?? "";
        if (BypassPaths.Any(bp => path.StartsWith(bp, StringComparison.OrdinalIgnoreCase)))
        {
            await next(context);
            return;
        }

        // Skip if not authenticated. The cookie carries the account id; sessions issued before linked
        // identities carried a Discord id instead and simply need a fresh sign-in.
        string? subject = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(subject) || !Guid.TryParse(subject, out Guid userId))
        {
            await next(context);
            return;
        }

        // Resolve the user from the database and cache in HttpContext.Items
        UserStatements userStatements = context.RequestServices.GetRequiredService<UserStatements>();
        UserStatements.UserRow? dbUser = await userStatements.GetUserById(userId);

        // A suspended account is treated as signed out, so a session from before the suspension stops working at once.
        if (dbUser is not null && dbUser.SuspendedAt is null)
        {
            List<UserIdentityRef> identities = (await userStatements.GetIdentitiesForUser(dbUser.Id))
                .Select(identity => identity.ToRef())
                .ToList();

            context.Items[AuthenticatedUser.HttpContextKey] = new AuthenticatedUser(
                dbUser.Id,
                dbUser.Username,
                dbUser.GlobalName,
                dbUser.AvatarUrl,
                dbUser.Email,
                dbUser.OnboardingCompletedAt is not null,
                UserRoles.Parse(dbUser.Role),
                GrantedTiers.Parse(dbUser.GrantedTier),
                identities);
        }

        await next(context);
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
