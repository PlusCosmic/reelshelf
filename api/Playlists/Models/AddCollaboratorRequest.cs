namespace Reelshelf.Playlists.Models;

public record AddCollaboratorRequest(Guid? UserId = null, string? Username = null);
