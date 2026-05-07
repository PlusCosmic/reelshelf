namespace Reelshelf.Playlists.Models;

public record AddClipToPlaylistRequest(Guid? ClipId = null, List<Guid>? ClipIds = null);
