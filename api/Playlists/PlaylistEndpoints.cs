using Microsoft.AspNetCore.Http.HttpResults;
using Reelshelf.Playlists.Models;
using Reelshelf.Auth;
using Reelshelf.Exceptions;
using Reelshelf.Games;

namespace Reelshelf.Playlists;

public static class PlaylistEndpoints
{
    public static void MapPlaylistEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("playlists")
            .RequireAuthorization();

        group.MapPost("", CreatePlaylist).WithName("CreatePlaylist")
            .RequirePermission(Permissions.PlaylistsManage);
        group.MapPost("gaming-session", CreateGamingSessionPlaylist).WithName("CreateGamingSessionPlaylist")
            .RequirePermission(Permissions.PlaylistsManage);
        group.MapGet("", GetPlaylists).WithName("GetPlaylists");
        group.MapGet("{id:guid}", GetPlaylistById).WithName("GetPlaylistById");
        group.MapPut("{id:guid}", UpdatePlaylist).WithName("UpdatePlaylist")
            .RequirePermission(Permissions.PlaylistsManage);
        group.MapDelete("{id:guid}", DeletePlaylist).WithName("DeletePlaylist")
            .RequirePermission(Permissions.PlaylistsManage);

        // Playlist clips endpoints
        group.MapPost("{id:guid}/clips", AddClipsToPlaylist).WithName("AddClipsToPlaylist")
            .RequirePermission(Permissions.PlaylistsManage);
        group.MapDelete("{id:guid}/clips/{clipId:guid}", RemoveClipFromPlaylist).WithName("RemoveClipFromPlaylist")
            .RequirePermission(Permissions.PlaylistsManage);
        group.MapPut("{id:guid}/clips/reorder", ReorderPlaylistClips).WithName("ReorderPlaylistClips")
            .RequirePermission(Permissions.PlaylistsManage);

        // Playlist collaborators endpoints
        group.MapPost("{id:guid}/collaborators", AddCollaboratorToPlaylist).WithName("AddCollaborator")
            .RequirePermission(Permissions.PlaylistsManage);
        group.MapDelete("{id:guid}/collaborators/{userId:guid}", RemoveCollaboratorFromPlaylist).WithName("RemoveCollaborator")
            .RequirePermission(Permissions.PlaylistsManage);
        group.MapGet("{id:guid}/collaborators", GetPlaylistCollaborators).WithName("GetCollaborators");
    }

    private static async Task<Results<Created<Playlist>, BadRequest<string>>> CreatePlaylist(
        PlaylistService playlistService,
        CreatePlaylistRequest request,
        AuthenticatedUser user)
    {
        try
        {
            Playlist playlist = await playlistService.CreatePlaylist(request.Name, request.Description, user.DiscordId);
            return TypedResults.Created($"/api/playlists/{playlist.Id}", playlist);
        }
        catch (BadRequestException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
    }

    private static async Task<Results<Created<PlaylistWithDetails>, BadRequest<string>>> CreateGamingSessionPlaylist(
        PlaylistService playlistService,
        GameCategoryStatements gameCategoryStatements,
        CreateGamingSessionPlaylistRequest request,
        AuthenticatedUser user)
    {
        try
        {
            GameCategory? category = await gameCategoryStatements.GetByIdAsync(request.CategoryId);
            if (category is null)
            {
                return TypedResults.BadRequest("Category not found");
            }

            PlaylistWithDetails playlist = await playlistService.CreateGamingSessionPlaylist(
                request.Participants,
                request.CategoryId,
                user.DiscordId,
                category.Name);

            return TypedResults.Created($"/api/playlists/{playlist.Id}", playlist);
        }
        catch (BadRequestException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
    }

    private static async Task<Ok<List<PlaylistSummary>>> GetPlaylists(
        PlaylistService playlistService,
        AuthenticatedUser user)
    {
        List<PlaylistSummary> playlists = await playlistService.GetPlaylistsForUser(user.DiscordId);
        return TypedResults.Ok(playlists);
    }

    private static async Task<Results<Ok<PlaylistWithDetails>, NotFound>> GetPlaylistById(
        PlaylistService playlistService,
        Guid id,
        AuthenticatedUser user)
    {
        PlaylistWithDetails? playlist = await playlistService.GetPlaylistById(id, user.DiscordId);
        if (playlist is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(playlist);
    }

    private static async Task<Results<Ok<Playlist>, NotFound, BadRequest<string>>> UpdatePlaylist(
        PlaylistService playlistService,
        Guid id,
        UpdatePlaylistRequest request,
        AuthenticatedUser user)
    {
        try
        {
            Playlist? playlist = await playlistService.UpdatePlaylist(id, user.DiscordId, request.Name, request.Description);
            if (playlist is null)
            {
                return TypedResults.NotFound();
            }

            return TypedResults.Ok(playlist);
        }
        catch (BadRequestException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
    }

    private static async Task<Results<NoContent, NotFound>> DeletePlaylist(
        PlaylistService playlistService,
        Guid id,
        AuthenticatedUser user)
    {
        bool deleted = await playlistService.DeletePlaylist(id, user.DiscordId);
        if (!deleted)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<PlaylistWithDetails>, NotFound, BadRequest<string>>> AddClipsToPlaylist(
        PlaylistService playlistService,
        Guid id,
        AddClipToPlaylistRequest request,
        AuthenticatedUser user)
    {
        try
        {
            PlaylistWithDetails? playlist;

            if (request.ClipId.HasValue)
            {
                playlist = await playlistService.AddClipToPlaylist(id, request.ClipId.Value, user.DiscordId);
            }
            else if (request.ClipIds is { Count: > 0 })
            {
                playlist = await playlistService.AddClipsToPlaylist(id, request.ClipIds, user.DiscordId);
            }
            else
            {
                return TypedResults.BadRequest("Either clipId or clipIds must be provided");
            }

            if (playlist is null)
            {
                return TypedResults.NotFound();
            }

            return TypedResults.Ok(playlist);
        }
        catch (BadRequestException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
    }

    private static async Task<Results<NoContent, NotFound>> RemoveClipFromPlaylist(
        PlaylistService playlistService,
        Guid id,
        Guid clipId,
        AuthenticatedUser user)
    {
        bool removed = await playlistService.RemoveClipFromPlaylist(id, clipId, user.DiscordId);
        if (!removed)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<PlaylistWithDetails>, NotFound, BadRequest<string>>> ReorderPlaylistClips(
        PlaylistService playlistService,
        Guid id,
        ReorderPlaylistClipsRequest request,
        AuthenticatedUser user)
    {
        if (request.ClipOrdering is not { Count: > 0 })
        {
            return TypedResults.BadRequest("clipOrdering must be provided and cannot be empty");
        }

        PlaylistWithDetails? playlist = await playlistService.ReorderPlaylistClips(id, request.ClipOrdering, user.DiscordId);
        if (playlist is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(playlist);
    }

    private static async Task<Results<Ok<List<PlaylistCollaborator>>, NotFound, BadRequest<string>>> AddCollaboratorToPlaylist(
        PlaylistService playlistService,
        Guid id,
        AddCollaboratorRequest request,
        AuthenticatedUser user)
    {
        if (!request.UserId.HasValue && string.IsNullOrWhiteSpace(request.Username))
        {
            return TypedResults.BadRequest("Either userId or username must be provided");
        }

        try
        {
            List<PlaylistCollaborator>? collaborators = await playlistService.AddCollaborator(
                id,
                user.DiscordId,
                request.UserId,
                request.Username
            );

            if (collaborators is null)
            {
                return TypedResults.NotFound();
            }

            return TypedResults.Ok(collaborators);
        }
        catch (BadRequestException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
    }

    private static async Task<Results<NoContent, NotFound, BadRequest<string>>> RemoveCollaboratorFromPlaylist(
        PlaylistService playlistService,
        Guid id,
        Guid userId,
        AuthenticatedUser user)
    {
        try
        {
            bool removed = await playlistService.RemoveCollaborator(id, userId, user.DiscordId);
            if (!removed)
            {
                return TypedResults.NotFound();
            }

            return TypedResults.NoContent();
        }
        catch (BadRequestException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
    }

    private static async Task<Results<Ok<List<PlaylistCollaborator>>, NotFound>> GetPlaylistCollaborators(
        PlaylistService playlistService,
        Guid id,
        AuthenticatedUser user)
    {
        List<PlaylistCollaborator>? collaborators = await playlistService.GetCollaborators(id, user.DiscordId);
        if (collaborators is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(collaborators);
    }
}
