using Reelshelf.Auth;
using Xunit;

namespace Reelshelf.Test.Auth;

public class PermissionsTests
{
    private static AuthenticatedUser UserWith(UserRole role)
    {
        return new AuthenticatedUser(Guid.NewGuid(), "user", null, null, null, true, role, null, []);
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
    public void AdminGrantsEverything()
    {
        AuthenticatedUser admin = UserWith(UserRole.Admin);

        Assert.True(admin.HasPermission(Permissions.AdminUsers));
        Assert.True(admin.HasPermission(Permissions.ClipsCreate));
    }

    [Theory]
    [InlineData("Admin", UserRole.Admin)]
    [InlineData("editor", UserRole.Editor)]
    [InlineData("VIEWER", UserRole.Viewer)]
    public void StoredRoles_ParseByName(string stored, UserRole expected)
    {
        Assert.Equal(expected, UserRoles.Parse(stored));
    }

    [Theory]
    [InlineData("2")]
    [InlineData("Owner")]
    [InlineData("")]
    [InlineData(null)]
    public void UnknownStoredRoles_AreViewer(string? stored)
    {
        Assert.Equal(UserRole.Viewer, UserRoles.Parse(stored));
        Assert.Null(UserRoles.TryParse(stored));
    }
}
