using Microsoft.AspNetCore.Http;

namespace Nucleus.Shared.Auth;

/// <summary>
/// Represents the currently authenticated user, resolved from the database.
/// Use this as an endpoint parameter to automatically inject the authenticated user.
/// The AuthenticatedUserMiddleware must be registered for this to work.
/// </summary>
public record AuthenticatedUser(
    Guid Id,
    string DiscordId,
    string Username,
    string? GlobalName,
    string? Avatar,
    UserRole Role,
    HashSet<string> AdditionalPermissions)
{
    internal const string HttpContextKey = "Nucleus.AuthenticatedUser";

    /// <summary>
    /// Checks if the user has a specific permission.
    /// Returns true if the user has the wildcard permission (*), the permission from their role defaults,
    /// or the permission was explicitly granted as an additional permission.
    /// </summary>
    public bool HasPermission(string permission)
    {
        // Get default permissions for the user's role
        HashSet<string> rolePermissions = Permissions.GetRolePermissions(Role);

        // Check for wildcard (admin has all permissions)
        if (rolePermissions.Contains(Permissions.All))
            return true;

        // Check role defaults
        if (rolePermissions.Contains(permission))
            return true;

        // Check additional permissions
        if (AdditionalPermissions.Contains(permission))
            return true;

        // Check if additional permissions include wildcard
        if (AdditionalPermissions.Contains(Permissions.All))
            return true;

        return false;
    }

    /// <summary>
    /// Checks if the user has any of the specified permissions.
    /// </summary>
    public bool HasAnyPermission(params string[] permissions)
    {
        return permissions.Any(HasPermission);
    }

    /// <summary>
    /// Checks if the user has all of the specified permissions.
    /// </summary>
    public bool HasAllPermissions(params string[] permissions)
    {
        return permissions.All(HasPermission);
    }

    /// <summary>
    /// Gets all effective permissions for this user (role defaults + additional).
    /// </summary>
    public HashSet<string> GetEffectivePermissions()
    {
        HashSet<string> effective = Permissions.GetRolePermissions(Role);
        foreach (string permission in AdditionalPermissions)
        {
            effective.Add(permission);
        }
        return effective;
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

    /// <summary>
    /// Constructs the full Discord CDN avatar URL for this user.
    /// </summary>
    public string GetAvatarUrl() => $"https://cdn.discordapp.com/avatars/{DiscordId}/{Avatar}";
}
