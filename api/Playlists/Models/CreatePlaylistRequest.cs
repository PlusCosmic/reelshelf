namespace Reelshelf.Playlists.Models;

public record CreatePlaylistRequest(string Name, string? Description = null);
