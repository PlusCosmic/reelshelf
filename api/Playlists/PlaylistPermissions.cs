namespace Reelshelf.Playlists;

/// <summary>
/// Who may do what to a shared collection. The person who made it manages it: renaming, deleting, inviting and
/// removing people. Everyone on it curates: adding, removing and reordering clips. Anyone may leave, except
/// the owner, who deletes the collection instead.
/// </summary>
public static class PlaylistPermissions
{
    public static bool CanManage(Guid creatorUserId, Guid actingUserId) => creatorUserId == actingUserId;

    public static bool CanRemoveCollaborator(Guid creatorUserId, Guid actingUserId, Guid targetUserId)
    {
        if (targetUserId == creatorUserId) return false;
        return actingUserId == creatorUserId || actingUserId == targetUserId;
    }
}
