using Reelshelf.Core.Models;

namespace Reelshelf.Playlists.Models;

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
    int CollaboratorCount,
    bool IsGamingSession,
    int TotalSeconds,
    List<PlaylistPreviewClip> PreviewClips,
    List<PlaylistGameCount> Games,
    List<PlaylistPerson> People
);

/// <summary>One of the first few clips in a collection's order, enough to show its thumbnail.</summary>
public record PlaylistPreviewClip(Guid ClipId, Guid VideoId);

/// <summary>How many of a collection's clips come from one game.</summary>
public record PlaylistGameCount(Guid GameCategoryId, int ClipCount);

/// <summary>Someone on a collection, as the collections list shows them.</summary>
public record PlaylistPerson(Guid UserId, string Username, string? AvatarUrl);
