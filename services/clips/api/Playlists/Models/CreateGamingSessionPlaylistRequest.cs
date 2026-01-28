namespace Nucleus.Clips.Playlists.Models;

public record CreateGamingSessionPlaylistRequest(List<Guid> Participants, Guid CategoryId)
{
}
