using Reelshelf.Core;
using Reelshelf.Core.Models;
using Reelshelf.Discord;
using Reelshelf.Playlists.Models;

namespace Reelshelf.Playlists;

public class GamingSessionPlaylistService(
    PlaylistService playlistService,
    PlaylistStatements playlistStatements,
    PlaylistAccess playlistAccess,
    DiscordStatements discordStatements,
    ClipService clipService)
{
    private const int SessionLookbackDays = 1;
    private const int ClipPageSize = 100;

    public async Task<PlaylistWithDetails> CreateGamingSessionPlaylist(
        List<Guid> participantIds,
        Guid gameCategoryId,
        string discordUserId,
        string categoryName)
    {
        DiscordStatements.DiscordUserRow currentUser = await playlistAccess.GetUser(discordUserId);
        List<Guid> participants = IncludeCurrentUser(participantIds, currentUser.Id);

        string playlistName = $"{categoryName} Session - {DateTimeOffset.UtcNow:MMMM dd}";
        Playlist playlist = await playlistService.CreatePlaylist(playlistName, string.Empty, discordUserId);

        DateTimeOffset sessionStart = DateTimeOffset.UtcNow.AddDays(-SessionLookbackDays);
        DateTimeOffset sessionEnd = DateTimeOffset.UtcNow;
        List<Clip> clips = [];

        foreach (Guid participantId in participants)
        {
            DiscordStatements.DiscordUserRow? participant = await discordStatements.GetUserById(participantId);
            if (participant == null)
            {
                continue;
            }

            if (participant.Id != currentUser.Id)
            {
                await playlistStatements.AddCollaborator(playlist.Id, participant.Id, currentUser.Id);
            }

            clips.AddRange(await GetSessionClipsForParticipant(
                gameCategoryId,
                participant.DiscordId,
                sessionStart,
                sessionEnd));
        }

        List<Guid> orderedClipIds = clips
            .OrderBy(clip => clip.CreatedAt)
            .Select(clip => clip.ClipId)
            .ToList();

        if (orderedClipIds.Count > 0)
        {
            await AddClipsToPlaylist(playlist.Id, orderedClipIds, currentUser.Id);
        }

        return await playlistService.GetPlaylistById(playlist.Id, discordUserId)
               ?? throw new InvalidOperationException("Failed to retrieve created playlist");
    }

    private async Task<List<Clip>> GetSessionClipsForParticipant(
        Guid gameCategoryId,
        string discordUserId,
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
                discordUserId,
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

    private static List<Guid> IncludeCurrentUser(List<Guid> participantIds, Guid currentUserId)
    {
        List<Guid> participants = participantIds.ToList();
        if (!participants.Contains(currentUserId))
        {
            participants.Add(currentUserId);
        }

        return participants;
    }
}
