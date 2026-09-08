using Reelshelf.Users;
using Reelshelf.Exceptions;

namespace Reelshelf.Playlists;

public class PlaylistAccess(
    PlaylistStatements playlistStatements,
    UserStatements userStatements)
{
    public async Task<UserStatements.UserRow> GetUser(Guid userId)
    {
        return await userStatements.GetUserById(userId)
               ?? throw new UnauthorizedException("User not found");
    }

    public async Task<PlaylistActor?> GetCollaborator(Guid playlistId, Guid userId)
    {
        UserStatements.UserRow user = await GetUser(userId);
        bool isCollaborator = await playlistStatements.IsUserCollaborator(playlistId, user.Id);
        return isCollaborator ? new PlaylistActor(user) : null;
    }
}

public sealed record PlaylistActor(UserStatements.UserRow User)
{
    public Guid UserId => User.Id;
}
