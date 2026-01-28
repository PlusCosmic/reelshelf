namespace Nucleus.Shared.Auth;

/// <summary>
/// Base permission definitions shared across all applications.
/// Applications should extend this with their own domain-specific permissions.
/// </summary>
public static class Permissions
{
    /// <summary>
    /// Wildcard permission - grants all permissions.
    /// </summary>
    public const string All = "*";

    // Clips permissions
    public const string ClipsRead = "clips.read";
    public const string ClipsCreate = "clips.create";
    public const string ClipsEdit = "clips.edit";
    public const string ClipsDelete = "clips.delete";

    // Playlist permissions
    public const string PlaylistsRead = "playlists.read";
    public const string PlaylistsManage = "playlists.manage";

    // Links permissions
    public const string LinksRead = "links.read";
    public const string LinksManage = "links.manage";

    // Minecraft permissions
    public const string MinecraftStatus = "minecraft.status";
    public const string MinecraftConsole = "minecraft.console";
    public const string MinecraftFiles = "minecraft.files";

    // Admin permissions
    public const string AdminUsers = "admin.users";

    /// <summary>
    /// Default permissions for each role.
    /// Applications should call RegisterRolePermissions to add domain-specific permissions.
    /// </summary>
    private static readonly Dictionary<UserRole, HashSet<string>> _roleDefaults = new()
    {
        [UserRole.Viewer] =
        [
            ClipsRead,
            PlaylistsRead,
            LinksRead,
            MinecraftStatus
        ],
        [UserRole.Editor] =
        [
            ClipsRead,
            PlaylistsRead,
            LinksRead,
            MinecraftStatus,
            ClipsCreate,
            ClipsEdit,
            PlaylistsManage,
            LinksManage
        ],
        [UserRole.Admin] = [All]
    };

    /// <summary>
    /// Gets the read-only role defaults.
    /// </summary>
    public static IReadOnlyDictionary<UserRole, HashSet<string>> RoleDefaults => _roleDefaults;

    /// <summary>
    /// Registers additional permissions for a role.
    /// Call this at application startup to add domain-specific permissions.
    /// </summary>
    public static void RegisterRolePermissions(UserRole role, params string[] permissions)
    {
        if (!_roleDefaults.TryGetValue(role, out var existing))
        {
            existing = [];
            _roleDefaults[role] = existing;
        }

        foreach (string permission in permissions)
        {
            existing.Add(permission);
        }
    }

    /// <summary>
    /// Gets the default permissions for a role.
    /// </summary>
    public static HashSet<string> GetRolePermissions(UserRole role)
    {
        return _roleDefaults.TryGetValue(role, out var permissions)
            ? new HashSet<string>(permissions)
            : [];
    }
}
