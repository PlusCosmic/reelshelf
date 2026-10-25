using Reelshelf.Auth;
using Xunit;

namespace Reelshelf.Test.Auth;

public class PermissionsTests
{
    private static AuthenticatedUser UserWith(UserRole role, params string[] additional)
    {
        return new AuthenticatedUser(Guid.NewGuid(), "user", null, null, null, true, role, [.. additional], []);
    }

    [Fact]
    public void ViewerHasNoWritePermissions()
    {
        AuthenticatedUser viewer = UserWith(UserRole.Viewer);

        Assert.False(viewer.HasPermission(Permissions.ClipsCreate));
        Assert.False(viewer.HasPermission(Permissions.PlaylistsManage));
        Assert.False(viewer.HasPermission(Permissions.AdminUsers));
    }

    [Fact]
    public void EditorCanWriteButIsNotAdmin()
    {
        AuthenticatedUser editor = UserWith(UserRole.Editor);

        Assert.True(editor.HasPermission(Permissions.ClipsCreate));
        Assert.True(editor.HasPermission(Permissions.ClipsEdit));
        Assert.True(editor.HasPermission(Permissions.ClipsDelete));
        Assert.True(editor.HasPermission(Permissions.PlaylistsManage));
        Assert.False(editor.HasPermission(Permissions.AdminUsers));
    }

    [Fact]
    public void AdminAndWildcardGrantEverything()
    {
        Assert.True(UserWith(UserRole.Admin).HasPermission(Permissions.AdminUsers));
        Assert.True(UserWith(UserRole.Viewer, Permissions.All).HasPermission(Permissions.ClipsCreate));
        Assert.True(UserWith(UserRole.Viewer, Permissions.AdminUsers).HasPermission(Permissions.AdminUsers));
    }
}
