using Reelshelf.Core;
using Reelshelf.Playlists.Models;
using Reelshelf.Discord;
using Reelshelf.Exceptions;
using Reelshelf.Games;

namespace Reelshelf.Playlists;

public class PlaylistService(
    PlaylistStatements playlistStatements,
    DiscordStatements discordStatements,
    PlaylistAccess playlistAccess,
    ClipsStatements clipsStatements,
    GameCategoryStatements gameCategoryStatements,
    ClipProjection clipProjection)
{
    public async Task<Playlist> CreatePlaylist(string name, string? description, string discordUserId)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new BadRequestException("Playlist name is required");
        }

        if (name.Length > 255)
        {
            throw new BadRequestException("Playlist name cannot exceed 255 characters");
        }

        DiscordStatements.DiscordUserRow discordUser = await playlistAccess.GetUser(discordUserId);
        Guid userId = discordUser.Id;

        PlaylistStatements.PlaylistRow playlistRow = await playlistStatements.InsertPlaylist(name, description, userId);

        await playlistStatements.AddCollaborator(playlistRow.Id, userId, userId);

        return new Playlist(
            playlistRow.Id,
            playlistRow.Name,
            playlistRow.Description,
            playlistRow.CreatorUserId,
            playlistRow.CreatedAt,
            playlistRow.UpdatedAt
        );
    }

    public async Task<List<PlaylistSummary>> GetPlaylistsForUser(string discordUserId)
    {
        DiscordStatements.DiscordUserRow discordUser = await playlistAccess.GetUser(discordUserId);
        Guid userId = discordUser.Id;

        List<PlaylistStatements.PlaylistSummaryRow> playlistRows = await playlistStatements.GetPlaylistsByUserId(userId);

        return playlistRows.Select(p => new PlaylistSummary(
            p.Id,
            p.Name,
            p.Description,
            p.CreatorUserId,
            p.CreatedAt,
            p.UpdatedAt,
            p.ClipCount,
            p.CollaboratorCount
        )).ToList();
    }

    public async Task<PlaylistWithDetails?> GetPlaylistById(Guid playlistId, string discordUserId)
    {
        PlaylistActor? actor = await playlistAccess.GetCollaborator(playlistId, discordUserId);
        if (actor is null)
        {
            return null;
        }

        PlaylistStatements.PlaylistRow? playlistRow = await playlistStatements.GetPlaylistById(playlistId);
        if (playlistRow == null)
        {
            return null;
        }

        List<PlaylistStatements.PlaylistCollaboratorRow> collaboratorRows =
            await playlistStatements.GetPlaylistCollaborators(playlistId);
        List<PlaylistCollaborator> collaborators = collaboratorRows.Select(c => new PlaylistCollaborator(
            c.UserId,
            c.Username,
            GetDiscordAvatarUrl(c.DiscordId, c.AvatarUrl),
            c.AddedAt,
            c.AddedByUserId
        )).ToList();

        List<PlaylistStatements.PlaylistClipRow> clipRows = await playlistStatements.GetPlaylistClips(playlistId);
        List<PlaylistClip> clips = [];
        foreach (PlaylistStatements.PlaylistClipRow clipRow in clipRows)
        {
            ClipsStatements.ClipWithTagsRow? clipWithTags = await clipsStatements.GetClipWithTagsById(clipRow.ClipId);
            Core.Models.Clip? clipDetails = null;
            if (clipWithTags is not null)
            {
                GameCategory? gameCategory = await gameCategoryStatements.GetByIdAsync(clipWithTags.GameCategoryId);
                ClipsStatements.ClipCollectionRow? clipCollection =
                    await clipsStatements.GetCollectionByOwnerAndCategory(clipWithTags.OwnerId, clipWithTags.GameCategoryId);
                if (gameCategory is not null && clipCollection is not null)
                {
                    bool isViewed = await clipsStatements.IsClipViewed(actor.UserId, clipRow.ClipId);
                    ClipsStatements.ClipShareRow? share = await clipsStatements.GetActiveShareByClipId(clipRow.ClipId);
                    clipDetails = clipProjection.ProjectClip(clipWithTags, gameCategory, clipCollection, isViewed, share is not null);
                }
            }

            clips.Add(new PlaylistClip(
                clipRow.Id,
                clipRow.ClipId,
                clipRow.Position,
                clipRow.AddedByUserId,
                clipRow.AddedAt,
                clipDetails
            ));
        }

        return new PlaylistWithDetails(
            playlistRow.Id,
            playlistRow.Name,
            playlistRow.Description,
            playlistRow.CreatorUserId,
            playlistRow.CreatedAt,
            playlistRow.UpdatedAt,
            collaborators,
            clips
        );
    }

    public async Task<Playlist?> UpdatePlaylist(Guid playlistId, string discordUserId, string? name, string? description)
    {
        if (name != null && string.IsNullOrWhiteSpace(name))
        {
            throw new BadRequestException("Playlist name cannot be empty");
        }

        if (name != null && name.Length > 255)
        {
            throw new BadRequestException("Playlist name cannot exceed 255 characters");
        }

        PlaylistActor? actor = await playlistAccess.GetCollaborator(playlistId, discordUserId);
        if (actor is null)
        {
            return null;
        }

        await playlistStatements.UpdatePlaylist(playlistId, name, description);

        PlaylistStatements.PlaylistRow? playlistRow = await playlistStatements.GetPlaylistById(playlistId);
        if (playlistRow == null)
        {
            return null;
        }

        return new Playlist(
            playlistRow.Id,
            playlistRow.Name,
            playlistRow.Description,
            playlistRow.CreatorUserId,
            playlistRow.CreatedAt,
            playlistRow.UpdatedAt
        );
    }

    public async Task<bool> DeletePlaylist(Guid playlistId, string discordUserId)
    {
        PlaylistActor? actor = await playlistAccess.GetCollaborator(playlistId, discordUserId);
        if (actor is null)
        {
            return false;
        }

        await playlistStatements.DeletePlaylist(playlistId);
        return true;
    }

    public async Task<PlaylistWithDetails?> AddClipToPlaylist(Guid playlistId, Guid clipId, string discordUserId)
    {
        PlaylistActor? actor = await playlistAccess.GetCollaborator(playlistId, discordUserId);
        if (actor is null)
        {
            return null;
        }

        ClipsStatements.ClipRow? clip = await clipsStatements.GetClipById(clipId);
        if (clip == null)
        {
            throw new BadRequestException("Clip not found");
        }

        bool exists = await playlistStatements.ClipExistsInPlaylist(playlistId, clipId);
        if (exists)
        {
            throw new BadRequestException("Clip is already in this playlist");
        }

        int maxPosition = await playlistStatements.GetMaxPosition(playlistId);
        int newPosition = maxPosition + 1;

        await playlistStatements.AddClipToPlaylist(playlistId, clipId, actor.UserId, newPosition);

        await playlistStatements.TouchPlaylistUpdatedAt(playlistId);

        return await GetPlaylistById(playlistId, discordUserId);
    }

    public async Task<PlaylistWithDetails?> AddClipsToPlaylist(Guid playlistId, List<Guid> clipIds, string discordUserId)
    {
        PlaylistActor? actor = await playlistAccess.GetCollaborator(playlistId, discordUserId);
        if (actor is null)
        {
            return null;
        }

        int maxPosition = await playlistStatements.GetMaxPosition(playlistId);
        int currentPosition = maxPosition + 1;

        foreach (Guid clipId in clipIds)
        {
            ClipsStatements.ClipRow? clip = await clipsStatements.GetClipById(clipId);
            if (clip == null)
            {
                throw new BadRequestException($"Clip {clipId} not found");
            }

            bool exists = await playlistStatements.ClipExistsInPlaylist(playlistId, clipId);
            if (!exists)
            {
                await playlistStatements.AddClipToPlaylist(playlistId, clipId, actor.UserId, currentPosition);
                currentPosition++;
            }
        }

        await playlistStatements.TouchPlaylistUpdatedAt(playlistId);

        return await GetPlaylistById(playlistId, discordUserId);
    }

    public async Task<bool> RemoveClipFromPlaylist(Guid playlistId, Guid clipId, string discordUserId)
    {
        PlaylistActor? actor = await playlistAccess.GetCollaborator(playlistId, discordUserId);
        if (actor is null)
        {
            return false;
        }

        bool exists = await playlistStatements.ClipExistsInPlaylist(playlistId, clipId);
        if (!exists)
        {
            return false;
        }

        await playlistStatements.RemoveClipFromPlaylist(playlistId, clipId);

        await playlistStatements.TouchPlaylistUpdatedAt(playlistId);

        return true;
    }

    public async Task<PlaylistWithDetails?> ReorderPlaylistClips(Guid playlistId, List<Guid> clipOrdering, string discordUserId)
    {
        PlaylistActor? actor = await playlistAccess.GetCollaborator(playlistId, discordUserId);
        if (actor is null)
        {
            return null;
        }

        await playlistStatements.ReorderClips(playlistId, clipOrdering);

        await playlistStatements.TouchPlaylistUpdatedAt(playlistId);

        return await GetPlaylistById(playlistId, discordUserId);
    }

    public async Task<List<PlaylistCollaborator>?> AddCollaborator(Guid playlistId, string discordUserId, Guid? userId, string? username)
    {
        PlaylistActor? actor = await playlistAccess.GetCollaborator(playlistId, discordUserId);
        if (actor is null)
        {
            return null;
        }

        DiscordStatements.DiscordUserRow? userToAdd = null;

        if (userId.HasValue)
        {
            userToAdd = await discordStatements.GetUserById(userId.Value);
        }
        else if (!string.IsNullOrWhiteSpace(username))
        {
            userToAdd = await discordStatements.GetUserByUsername(username);
        }

        if (userToAdd == null)
        {
            throw new BadRequestException("User not found");
        }

        await playlistStatements.AddCollaborator(playlistId, userToAdd.Id, actor.UserId);

        List<PlaylistStatements.PlaylistCollaboratorRow> collaboratorRows =
            await playlistStatements.GetPlaylistCollaborators(playlistId);

        return collaboratorRows.Select(c => new PlaylistCollaborator(
            c.UserId,
            c.Username,
            GetDiscordAvatarUrl(c.DiscordId, c.AvatarUrl),
            c.AddedAt,
            c.AddedByUserId
        )).ToList();
    }

    public async Task<bool> RemoveCollaborator(Guid playlistId, Guid collaboratorUserId, string discordUserId)
    {
        PlaylistActor? actor = await playlistAccess.GetCollaborator(playlistId, discordUserId);
        if (actor is null)
        {
            return false;
        }

        bool targetIsCollaborator = await playlistStatements.IsUserCollaborator(playlistId, collaboratorUserId);
        if (!targetIsCollaborator)
        {
            return false;
        }

        int collaboratorCount = await playlistStatements.GetCollaboratorCount(playlistId);
        if (collaboratorCount <= 1)
        {
            throw new BadRequestException("Cannot remove the last collaborator from a playlist");
        }

        await playlistStatements.RemoveCollaborator(playlistId, collaboratorUserId);

        return true;
    }

    public async Task<List<PlaylistCollaborator>?> GetCollaborators(Guid playlistId, string discordUserId)
    {
        PlaylistActor? actor = await playlistAccess.GetCollaborator(playlistId, discordUserId);
        if (actor is null)
        {
            return null;
        }

        List<PlaylistStatements.PlaylistCollaboratorRow> collaboratorRows =
            await playlistStatements.GetPlaylistCollaborators(playlistId);

        return collaboratorRows.Select(c => new PlaylistCollaborator(
            c.UserId,
            c.Username,
            GetDiscordAvatarUrl(c.DiscordId, c.AvatarUrl),
            c.AddedAt,
            c.AddedByUserId
        )).ToList();
    }

    private static string? GetDiscordAvatarUrl(string discordId, string? avatar)
    {
        return string.IsNullOrWhiteSpace(avatar)
            ? null
            : $"https://cdn.discordapp.com/avatars/{discordId}/{avatar}";
    }
}
