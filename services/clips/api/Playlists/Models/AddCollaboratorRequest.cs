namespace Nucleus.Clips.Playlists.Models;

public record AddCollaboratorRequest(Guid? UserId = null, string? Username = null);
