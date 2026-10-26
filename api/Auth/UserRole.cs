namespace Reelshelf.Auth;

/// <summary>
/// Defines the available user roles in the system.
/// Each role has a set of default permissions defined in <see cref="Permissions"/>.
/// </summary>
public enum UserRole
{
    /// <summary>
    /// No write permissions; reading needs only a signed-in account. Also the fallback for an unrecognised stored role.
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
