namespace Nucleus.Shared.Auth;

/// <summary>
/// Defines the available user roles in the system.
/// Each role has a set of default permissions defined in <see cref="Permissions.RoleDefaults"/>.
/// </summary>
public enum UserRole
{
    /// <summary>
    /// Read-only access to most resources.
    /// </summary>
    Viewer,

    /// <summary>
    /// Can create and edit content they own.
    /// </summary>
    Editor,

    /// <summary>
    /// Full access to all features and administrative functions.
    /// </summary>
    Admin
}
