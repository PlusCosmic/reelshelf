using Reelshelf.Playlists;
using Xunit;

namespace Reelshelf.Test.Playlists;

public class PlaylistPermissionsTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Sam = Guid.NewGuid();
    private static readonly Guid Robin = Guid.NewGuid();

    [Fact]
    public void OnlyTheOwnerManages()
    {
        Assert.True(PlaylistPermissions.CanManage(Owner, Owner));
        Assert.False(PlaylistPermissions.CanManage(Owner, Sam));
    }

    [Fact]
    public void TheOwnerCanRemoveAnyoneElse()
    {
        Assert.True(PlaylistPermissions.CanRemoveCollaborator(Owner, Owner, Sam));
    }

    [Fact]
    public void ACollaboratorCanLeave_ButNotRemoveOthers()
    {
        Assert.True(PlaylistPermissions.CanRemoveCollaborator(Owner, Sam, Sam));
        Assert.False(PlaylistPermissions.CanRemoveCollaborator(Owner, Sam, Robin));
    }

    [Fact]
    public void NobodyRemovesTheOwner()
    {
        Assert.False(PlaylistPermissions.CanRemoveCollaborator(Owner, Owner, Owner));
        Assert.False(PlaylistPermissions.CanRemoveCollaborator(Owner, Sam, Owner));
    }
}
