namespace Reelshelf.Playlists.Models;

public sealed record EnsureGamingSessionPlaylistRequest(
    Guid CategoryId,
    DateOnly SessionDate,
    List<Guid> ClipIds,
    string Timezone);

