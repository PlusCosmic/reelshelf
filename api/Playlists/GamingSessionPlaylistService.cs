using Reelshelf.Core;
using Reelshelf.Core.Models;
using Reelshelf.Users;
using Reelshelf.Exceptions;
using Reelshelf.Playlists.Models;

namespace Reelshelf.Playlists;

public class GamingSessionPlaylistService(
    PlaylistService playlistService,
    PlaylistStatements playlistStatements,
    UserStatements userStatements,
    ClipService clipService)
{
    private const int SessionLookbackDays = 1;
    /// <summary>Upper bound on people in one session, caller included.</summary>
    private const int MaxParticipants = 12;
    private const int ClipPageSize = 100;

    public async Task<PlaylistWithDetails> CreateGamingSessionPlaylist(
        List<Guid> participantIds,
        Guid gameCategoryId,
        Guid userId,
        string categoryName)
    {
                List<Guid> participants = IncludeCurrentUser(participantIds, userId);
        if (participants.Count > MaxParticipants)
        {
            throw new BadRequestException($"A session can include at most {MaxParticipants} participants");
        }

        // A session pulls each participant's recent clips into a playlist the caller controls, so a
        // participant must have opted in: they must have added the caller to a collection they created.
        // Anything the caller can do alone (creating a playlist, adding a collaborator) does not count.
        HashSet<Guid> allowedParticipants = (await userStatements.GetUsersWhoAddedMe(userId))
            .Select(peer => peer.Id)
            .ToHashSet();
        allowedParticipants.Add(userId);
        if (participants.Any(participantId => !allowedParticipants.Contains(participantId)))
        {
            throw new BadRequestException(
                "You can only start a session with people who have added you to one of their collections");
        }

        string playlistName = $"{categoryName} Session - {DateTimeOffset.UtcNow:MMMM dd}";
        Playlist playlist = await playlistService.CreatePlaylist(playlistName, string.Empty, userId);

        DateTimeOffset sessionStart = DateTimeOffset.UtcNow.AddDays(-SessionLookbackDays);
        DateTimeOffset sessionEnd = DateTimeOffset.UtcNow;
        List<Clip> clips = [];

        foreach (Guid participantId in participants)
        {
            UserStatements.UserRow? participant = await userStatements.GetUserById(participantId);
            if (participant == null)
            {
                continue;
            }

            if (participant.Id != userId)
            {
                await playlistStatements.AddCollaborator(playlist.Id, participant.Id, userId);
            }

            clips.AddRange(await GetSessionClipsForParticipant(
                gameCategoryId,
                participant.Id,
                sessionStart,
                sessionEnd));
        }

        List<Guid> orderedClipIds = clips
            .OrderBy(clip => clip.CreatedAt)
            .Select(clip => clip.ClipId)
            .ToList();

        if (orderedClipIds.Count > 0)
        {
            await AddClipsToPlaylist(playlist.Id, orderedClipIds, userId);
        }

        return await playlistService.GetPlaylistById(playlist.Id, userId)
               ?? throw new InvalidOperationException("Failed to retrieve created playlist");
    }

    private async Task<List<Clip>> GetSessionClipsForParticipant(
        Guid gameCategoryId,
        Guid userId,
        DateTimeOffset sessionStart,
        DateTimeOffset sessionEnd)
    {
        List<Clip> clips = [];
        int page = 1;
        int totalPages;

        do
        {
            PagedClipsResponse pageResult = await clipService.GetClipsForCategory(
                gameCategoryId,
                userId,
                page,
                ClipPageSize,
                null,
                null,
                false,
                ClipSortOrder.DateAscending,
                sessionStart,
                sessionEnd);

            clips.AddRange(pageResult.Clips);
            totalPages = (int)pageResult.TotalPages;
            page++;
        } while (page <= totalPages);

        return clips;
    }

    private async Task AddClipsToPlaylist(Guid playlistId, List<Guid> clipIds, Guid addedByUserId)
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

    /// <summary>
    /// Distinct participant ids with the caller included. Duplicates are collapsed before any lookups so a
    /// repeated id cannot multiply the per-participant library scans below.
    /// </summary>
    private static List<Guid> IncludeCurrentUser(List<Guid> participantIds, Guid currentUserId)
    {
        List<Guid> participants = participantIds.Distinct().ToList();
        if (!participants.Contains(currentUserId))
        {
            participants.Add(currentUserId);
        }

        return participants;
    }
}
