namespace Reelshelf.Playlists.Models;

public record CreateGamingSessionPlaylistRequest(List<Guid> Participants, Guid CategoryId)
{
}
