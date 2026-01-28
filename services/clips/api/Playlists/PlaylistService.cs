using Nucleus.Clips.Core;
using Nucleus.Clips.Core.Models;
using Nucleus.Clips.Playlists.Models;
using Nucleus.Shared.Discord;
using Nucleus.Shared.Exceptions;

namespace Nucleus.Clips.Playlists;

public class PlaylistService(
    PlaylistStatements playlistStatements,
    DiscordStatements discordStatements,
    ClipsStatements clipsStatements,
    ClipService clipService)
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

        DiscordStatements.DiscordUserRow discordUser = await discordStatements.GetUserByDiscordId(discordUserId)
                                                       ?? throw new UnauthorizedException("User not found");
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
        DiscordStatements.DiscordUserRow discordUser = await discordStatements.GetUserByDiscordId(discordUserId)
                                                       ?? throw new UnauthorizedException("User not found");
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
        DiscordStatements.DiscordUserRow discordUser = await discordStatements.GetUserByDiscordId(discordUserId)
                                                       ?? throw new UnauthorizedException("User not found");
        Guid userId = discordUser.Id;

        bool isCollaborator = await playlistStatements.IsUserCollaborator(playlistId, userId);
        if (!isCollaborator)
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
            c.AvatarUrl,
            c.AddedAt,
            c.AddedByUserId
        )).ToList();

        List<PlaylistStatements.PlaylistClipRow> clipRows = await playlistStatements.GetPlaylistClips(playlistId);
        List<PlaylistClip> clips = clipRows.Select(c => new PlaylistClip(
            c.Id,
            c.ClipId,
            c.Position,
            c.AddedByUserId,
            c.AddedAt,
            null
        )).ToList();

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

        DiscordStatements.DiscordUserRow discordUser = await discordStatements.GetUserByDiscordId(discordUserId)
                                                       ?? throw new UnauthorizedException("User not found");
        Guid userId = discordUser.Id;

        bool isCollaborator = await playlistStatements.IsUserCollaborator(playlistId, userId);
        if (!isCollaborator)
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
        DiscordStatements.DiscordUserRow discordUser = await discordStatements.GetUserByDiscordId(discordUserId)
                                                       ?? throw new UnauthorizedException("User not found");
        Guid userId = discordUser.Id;

        bool isCollaborator = await playlistStatements.IsUserCollaborator(playlistId, userId);
        if (!isCollaborator)
        {
            return false;
        }

        await playlistStatements.DeletePlaylist(playlistId);
        return true;
    }

    public async Task<PlaylistWithDetails?> AddClipToPlaylist(Guid playlistId, Guid clipId, string discordUserId)
    {
        DiscordStatements.DiscordUserRow discordUser = await discordStatements.GetUserByDiscordId(discordUserId)
                                                       ?? throw new UnauthorizedException("User not found");
        Guid userId = discordUser.Id;

        bool isCollaborator = await playlistStatements.IsUserCollaborator(playlistId, userId);
        if (!isCollaborator)
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

        await playlistStatements.AddClipToPlaylist(playlistId, clipId, userId, newPosition);

        await playlistStatements.TouchPlaylistUpdatedAt(playlistId);

        return await GetPlaylistById(playlistId, discordUserId);
    }

    public async Task<PlaylistWithDetails?> AddClipsToPlaylist(Guid playlistId, List<Guid> clipIds, string discordUserId)
    {
        DiscordStatements.DiscordUserRow discordUser = await discordStatements.GetUserByDiscordId(discordUserId)
                                                       ?? throw new UnauthorizedException("User not found");
        Guid userId = discordUser.Id;

        bool isCollaborator = await playlistStatements.IsUserCollaborator(playlistId, userId);
        if (!isCollaborator)
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
                await playlistStatements.AddClipToPlaylist(playlistId, clipId, userId, currentPosition);
                currentPosition++;
            }
        }

        await playlistStatements.TouchPlaylistUpdatedAt(playlistId);

        return await GetPlaylistById(playlistId, discordUserId);
    }

    public async Task<bool> RemoveClipFromPlaylist(Guid playlistId, Guid clipId, string discordUserId)
    {
        DiscordStatements.DiscordUserRow discordUser = await discordStatements.GetUserByDiscordId(discordUserId)
                                                       ?? throw new UnauthorizedException("User not found");
        Guid userId = discordUser.Id;

        bool isCollaborator = await playlistStatements.IsUserCollaborator(playlistId, userId);
        if (!isCollaborator)
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
        DiscordStatements.DiscordUserRow discordUser = await discordStatements.GetUserByDiscordId(discordUserId)
                                                       ?? throw new UnauthorizedException("User not found");
        Guid userId = discordUser.Id;

        bool isCollaborator = await playlistStatements.IsUserCollaborator(playlistId, userId);
        if (!isCollaborator)
        {
            return null;
        }

        await playlistStatements.ReorderClips(playlistId, clipOrdering);

        await playlistStatements.TouchPlaylistUpdatedAt(playlistId);

        return await GetPlaylistById(playlistId, discordUserId);
    }

    public async Task<List<PlaylistCollaborator>?> AddCollaborator(Guid playlistId, string discordUserId, Guid? userId, string? username)
    {
        DiscordStatements.DiscordUserRow discordUser = await discordStatements.GetUserByDiscordId(discordUserId)
                                                       ?? throw new UnauthorizedException("User not found");
        Guid currentUserId = discordUser.Id;

        bool isCollaborator = await playlistStatements.IsUserCollaborator(playlistId, currentUserId);
        if (!isCollaborator)
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

        await playlistStatements.AddCollaborator(playlistId, userToAdd.Id, currentUserId);

        List<PlaylistStatements.PlaylistCollaboratorRow> collaboratorRows =
            await playlistStatements.GetPlaylistCollaborators(playlistId);

        return collaboratorRows.Select(c => new PlaylistCollaborator(
            c.UserId,
            c.Username,
            c.AvatarUrl,
            c.AddedAt,
            c.AddedByUserId
        )).ToList();
    }

    public async Task<bool> RemoveCollaborator(Guid playlistId, Guid collaboratorUserId, string discordUserId)
    {
        DiscordStatements.DiscordUserRow discordUser = await discordStatements.GetUserByDiscordId(discordUserId)
                                                       ?? throw new UnauthorizedException("User not found");
        Guid currentUserId = discordUser.Id;

        bool isCollaborator = await playlistStatements.IsUserCollaborator(playlistId, currentUserId);
        if (!isCollaborator)
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
        DiscordStatements.DiscordUserRow discordUser = await discordStatements.GetUserByDiscordId(discordUserId)
                                                       ?? throw new UnauthorizedException("User not found");
        Guid userId = discordUser.Id;

        bool isCollaborator = await playlistStatements.IsUserCollaborator(playlistId, userId);
        if (!isCollaborator)
        {
            return null;
        }

        List<PlaylistStatements.PlaylistCollaboratorRow> collaboratorRows =
            await playlistStatements.GetPlaylistCollaborators(playlistId);

        return collaboratorRows.Select(c => new PlaylistCollaborator(
            c.UserId,
            c.Username,
            c.AvatarUrl,
            c.AddedAt,
            c.AddedByUserId
        )).ToList();
    }

    public async Task<PlaylistWithDetails> CreateGamingSessionPlaylist(
        List<Guid> participantIds,
        Guid gameCategoryId,
        string discordUserId,
        string categoryName)
    {
        DiscordStatements.DiscordUserRow currentUser = await discordStatements.GetUserByDiscordId(discordUserId)
                                                       ?? throw new UnauthorizedException("User not found");

        List<Guid> participants = participantIds.ToList();
        if (!participants.Contains(currentUser.Id))
        {
            participants.Add(currentUser.Id);
        }

        string playlistName = $"{categoryName} Session - {DateTimeOffset.UtcNow:MMMM dd}";
        Playlist playlist = await CreatePlaylist(playlistName, string.Empty, discordUserId);

        DateTimeOffset sessionStart = DateTimeOffset.UtcNow.AddDays(-1);
        DateTimeOffset sessionEnd = DateTimeOffset.UtcNow;
        List<Clip> allClips = [];

        foreach (Guid participantId in participants)
        {
            DiscordStatements.DiscordUserRow? participant = await discordStatements.GetUserById(participantId);
            if (participant == null)
            {
                continue;
            }

            if (participantId != currentUser.Id)
            {
                await AddCollaboratorInternal(playlist.Id, currentUser.Id, participantId);
            }

            PagedClipsResponse clipsForParticipant = await clipService.GetClipsForCategory(
                gameCategoryId,
                participant.DiscordId,
                1,
                int.MaxValue,
                null,
                null,
                false,
                ClipSortOrder.DateAscending,
                sessionStart,
                sessionEnd);

            allClips.AddRange(clipsForParticipant.Clips);
        }

        allClips = allClips.OrderBy(c => c.CreatedAt).ToList();

        if (allClips.Count > 0)
        {
            await AddClipsToPlaylistInternal(playlist.Id, allClips.Select(c => c.ClipId).ToList(), currentUser.Id);
        }

        return await GetPlaylistById(playlist.Id, discordUserId)
               ?? throw new InvalidOperationException("Failed to retrieve created playlist");
    }

    private async Task AddCollaboratorInternal(Guid playlistId, Guid addedByUserId, Guid userIdToAdd)
    {
        await playlistStatements.AddCollaborator(playlistId, userIdToAdd, addedByUserId);
    }

    private async Task AddClipsToPlaylistInternal(Guid playlistId, List<Guid> clipIds, Guid addedByUserId)
    {
        int maxPosition = await playlistStatements.GetMaxPosition(playlistId);
        int currentPosition = maxPosition + 1;

        foreach (Guid clipId in clipIds)
        {
            bool exists = await playlistStatements.ClipExistsInPlaylist(playlistId, clipId);
            if (!exists)
            {
                await playlistStatements.AddClipToPlaylist(playlistId, clipId, addedByUserId, currentPosition);
                currentPosition++;
            }
        }

        await playlistStatements.TouchPlaylistUpdatedAt(playlistId);
    }
}
