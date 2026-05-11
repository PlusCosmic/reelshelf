using Reelshelf.Core;
using Reelshelf.Discord;
using Reelshelf.Games;
using Reelshelf.Playlists.Models;

namespace Reelshelf.Playlists;

public class GamingSessionPlaylistStore(
    PlaylistAccess playlistAccess,
    PlaylistService playlistService,
    PlaylistStatements playlistStatements,
    ClipsStatements clipsStatements,
    GameCategoryStatements gameCategoryStatements) : IGamingSessionPlaylistStore
{
    public Task<DiscordStatements.DiscordUserRow> GetUser(string discordUserId)
    {
        return playlistAccess.GetUser(discordUserId);
    }

    public Task<GameCategory?> GetCategory(Guid categoryId)
    {
        return gameCategoryStatements.GetByIdAsync(categoryId);
    }

    public Task<ClipsStatements.ClipRow?> GetClip(Guid clipId)
    {
        return clipsStatements.GetClipById(clipId);
    }

    public async Task<GamingSessionPlaylistRow?> GetGamingSessionPlaylist(Guid ownerId, Guid categoryId, DateOnly sessionDate)
    {
        PlaylistStatements.GamingSessionPlaylistRow? row =
            await playlistStatements.GetGamingSessionPlaylist(ownerId, categoryId, sessionDate);

        return row is null
            ? null
            : new GamingSessionPlaylistRow(
                row.PlaylistId,
                row.OwnerId,
                row.GameCategoryId,
                row.SessionDate,
                row.Timezone);
    }

    public Task UpdateGamingSessionTimezone(Guid playlistId, string timezone)
    {
        return playlistStatements.UpdateGamingSessionTimezone(playlistId, timezone);
    }

    public Task<Playlist> CreatePlaylist(string name, string? description, string discordUserId)
    {
        return playlistService.CreatePlaylist(name, description, discordUserId);
    }

    public Task<Guid> UpsertGamingSessionPlaylist(Guid playlistId, Guid ownerId, Guid categoryId, DateOnly sessionDate, string timezone)
    {
        return playlistStatements.UpsertGamingSessionPlaylist(playlistId, ownerId, categoryId, sessionDate, timezone);
    }

    public Task DeletePlaylist(Guid playlistId)
    {
        return playlistStatements.DeletePlaylist(playlistId);
    }

    public Task<int> GetMaxPosition(Guid playlistId)
    {
        return playlistStatements.GetMaxPosition(playlistId);
    }

    public Task<bool> ClipExistsInPlaylist(Guid playlistId, Guid clipId)
    {
        return playlistStatements.ClipExistsInPlaylist(playlistId, clipId);
    }

    public async Task AddClipToPlaylist(Guid playlistId, Guid clipId, Guid addedByUserId, int position)
    {
        await playlistStatements.AddClipToPlaylist(playlistId, clipId, addedByUserId, position);
    }

    public Task TouchPlaylistUpdatedAt(Guid playlistId)
    {
        return playlistStatements.TouchPlaylistUpdatedAt(playlistId);
    }

    public Task<PlaylistWithDetails?> GetPlaylistDetails(Guid playlistId, string discordUserId)
    {
        return playlistService.GetPlaylistById(playlistId, discordUserId);
    }
}
