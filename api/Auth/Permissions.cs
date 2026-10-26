namespace Reelshelf.Auth;

/// <summary>
/// Permissions checked by <see cref="PermissionAuthorizationExtensions.RequirePermission{TBuilder}"/>.
/// Reading needs only a signed-in account; these gate writes and the admin pages.
/// </summary>
public static class Permissions
{
    /// <summary>
    /// Wildcard permission - grants all permissions.
    /// </summary>
    public const string All = "*";

    // Clips permissions
    public const string ClipsCreate = "clips.create";
    public const string ClipsEdit = "clips.edit";
    public const string ClipsDelete = "clips.delete";

    // Playlist permissions
    public const string PlaylistsManage = "playlists.manage";

    // Admin permissions
    public const string AdminUsers = "admin.users";

    /// <summary>
    /// Default permissions for each role.
    /// </summary>
    private static readonly Dictionary<UserRole, HashSet<string>> RoleDefaults = new()
    {
        [UserRole.Viewer] = [],
        [UserRole.Editor] =
        [
            ClipsCreate,
            ClipsEdit,
            ClipsDelete, // Editors must be able to free room in their plan.
            PlaylistsManage
        ],
        [UserRole.Admin] = [All]
    };

    /// <summary>
    /// Gets the default permissions for a role.
    /// </summary>
    public static HashSet<string> GetRolePermissions(UserRole role)
    {
        return RoleDefaults.TryGetValue(role, out var permissions)
            ? new HashSet<string>(permissions)
            : [];
    }
}
