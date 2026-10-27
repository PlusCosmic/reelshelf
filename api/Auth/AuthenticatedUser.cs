using Reelshelf.Billing;
using Reelshelf.Users;

namespace Reelshelf.Auth;

/// <summary>
/// Represents the currently authenticated user, resolved from the database.
/// Use this as an endpoint parameter to automatically inject the authenticated user.
/// The AuthenticatedUserMiddleware must be registered for this to work.
/// </summary>
public record AuthenticatedUser(
    Guid Id,
    string Username,
    string? GlobalName,
    string? AvatarUrl,
    string? Email,
    bool OnboardingCompleted,
    UserRole Role,
    AccountTier? GrantedTier,
    IReadOnlyList<UserIdentityRef> Identities)
{
    internal const string HttpContextKey = "Reelshelf.AuthenticatedUser";

    /// <summary>
    /// Checks if the user's role grants a permission: Admin's wildcard (*) grants every one.
    /// </summary>
    public bool HasPermission(string permission)
    {
        HashSet<string> rolePermissions = Permissions.GetRolePermissions(Role);
        return rolePermissions.Contains(Permissions.All) || rolePermissions.Contains(permission);
    }

    /// <summary>
    /// Binds the AuthenticatedUser from HttpContext.Items.
    /// This is called automatically by minimal APIs when AuthenticatedUser is used as a parameter.
    /// The AuthenticatedUserMiddleware populates this before binding occurs.
    /// </summary>
    public static ValueTask<AuthenticatedUser?> BindAsync(HttpContext context)
    {
        if (context.Items.TryGetValue(HttpContextKey, out var item) && item is AuthenticatedUser user)
        {
            return ValueTask.FromResult<AuthenticatedUser?>(user);
        }

        return ValueTask.FromResult<AuthenticatedUser?>(null);
    }
}
