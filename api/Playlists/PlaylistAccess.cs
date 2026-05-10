using Reelshelf.Discord;
using Reelshelf.Exceptions;

namespace Reelshelf.Playlists;

public class PlaylistAccess(
    PlaylistStatements playlistStatements,
    DiscordStatements discordStatements)
{
    public async Task<DiscordStatements.DiscordUserRow> GetUser(string discordUserId)
    {
        return await discordStatements.GetUserByDiscordId(discordUserId)
               ?? throw new UnauthorizedException("User not found");
    }

    public async Task<PlaylistActor?> GetCollaborator(Guid playlistId, string discordUserId)
    {
        DiscordStatements.DiscordUserRow user = await GetUser(discordUserId);
        bool isCollaborator = await playlistStatements.IsUserCollaborator(playlistId, user.Id);
        return isCollaborator ? new PlaylistActor(user) : null;
    }
}

public sealed record PlaylistActor(DiscordStatements.DiscordUserRow User)
{
    public Guid UserId => User.Id;
    public string DiscordId => User.DiscordId;
}
