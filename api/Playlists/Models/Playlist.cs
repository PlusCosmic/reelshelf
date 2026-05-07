using Nucleus.Clips.Core.Models;

namespace Nucleus.Clips.Playlists.Models;

public record Playlist(
    Guid Id,
    string Name,
    string? Description,
    Guid CreatorUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public record PlaylistCollaborator(
    Guid UserId,
    string Username,
    string? AvatarUrl,
    DateTimeOffset AddedAt,
    Guid AddedByUserId
);

public record PlaylistClip(
    Guid Id,
    Guid ClipId,
    int Position,
    Guid AddedByUserId,
    DateTimeOffset AddedAt,
    Clip? ClipDetails = null
);

public record PlaylistWithDetails(
    Guid Id,
    string Name,
    string? Description,
    Guid CreatorUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    List<PlaylistCollaborator> Collaborators,
    List<PlaylistClip> Clips
);

public record PlaylistSummary(
    Guid Id,
    string Name,
    string? Description,
    Guid CreatorUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int ClipCount,
    int CollaboratorCount
);
