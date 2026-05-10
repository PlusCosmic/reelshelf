using Microsoft.AspNetCore.Http.HttpResults;
using Reelshelf.Core.Models;
using Reelshelf.Auth;

namespace Reelshelf.Core;

public static class ClipsEndpoints
{
    public static void MapClipsEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("clips")
            .RequireAuthorization();

        group.MapGet("library", GetLibrary).WithName("GetClipLibrary");
        group.MapGet("categories/{categoryId:guid}/videos", GetVideosByCategory).WithName("GetVideosByCategory");
        group.MapPost("categories/{categoryId:guid}/videos", CreateVideo).WithName("CreateVideo")
            .RequirePermission(Permissions.ClipsCreate);
        group.MapGet("videos/{clipId:guid}", GetVideoById).WithName("GetVideoById");
        group.MapPost("videos/{clipId:guid}/share", ShareVideo).WithName("ShareVideo");
        group.MapPost("videos/{clipId:guid}/view", MarkVideoAsViewed).WithName("MarkVideoAsViewed");
        group.MapPost("videos/{clipId:guid}/tags", AddTagToClip).WithName("AddTagToClip")
            .RequirePermission(Permissions.ClipsEdit);
        group.MapDelete("videos/{clipId:guid}/tags/{tag}", RemoveTagFromClip).WithName("RemoveTagFromClip")
            .RequirePermission(Permissions.ClipsEdit);
        group.MapGet("tags/top", GetTopTags).WithName("GetTopTags");
        group.MapPatch("videos/{clipId:guid}/title", UpdateClipTitle).WithName("UpdateClipTitle")
            .RequirePermission(Permissions.ClipsEdit);
        group.MapDelete("videos/{clipId:guid}", DeleteClip).WithName("DeleteClip")
            .RequirePermission(Permissions.ClipsDelete);
        group.MapPost("backfill-metadata", BackfillClipMetadata).WithName("BackfillClipMetadata")
            .RequirePermission(Permissions.AdminUsers);
    }

    private static async Task<Ok<ClipLibraryResponse>> GetLibrary(
        ClipLibraryService libraryService,
        AuthenticatedUser user)
    {
        return TypedResults.Ok(await libraryService.GetLibrary(user.DiscordId));
    }

    private static async Task<Results<Ok<PagedClipsResponse>, BadRequest<string>>> GetVideosByCategory(
        ClipService clipService,
        Guid categoryId,
        AuthenticatedUser user,
        int page,
        int pageSize,
        string[]? tags = null,
        string? titleSearch = null,
        bool unviewedOnly = false,
        ClipSortOrder sortOrder = ClipSortOrder.DateDescending,
        DateTimeOffset? startDate = null,
        DateTimeOffset? endDate = null)
    {
        if (page < 1)
        {
            return TypedResults.BadRequest("Page must be greater than zero");
        }

        if (pageSize < 1 || pageSize > 100)
        {
            return TypedResults.BadRequest("Page size must be between 1 and 100");
        }

        List<string>? tagList = tags?.ToList();
        return TypedResults.Ok(
            await clipService.GetClipsForCategory(categoryId, user.DiscordId, page, pageSize, tagList, titleSearch, unviewedOnly, sortOrder, startDate, endDate));
    }

    private static async Task<Results<Ok<CreateClipResponse>, Conflict<string>>> CreateVideo(
        ClipService clipService,
        Guid categoryId,
        AuthenticatedUser user,
        string videoTitle,
        DateTimeOffset? createdAt = null,
        string? md5Hash = null)
    {
        CreateClipResponse? result = await clipService.CreateClip(categoryId, videoTitle, user.DiscordId, createdAt ?? DateTimeOffset.UtcNow, md5Hash);
        if (result is null)
        {
            return TypedResults.Conflict("A video with this MD5 hash already exists");
        }

        return TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<Clip>, NotFound>> GetVideoById(
        ClipService clipService,
        AuthenticatedUser user,
        Guid clipId)
    {
        Clip? clip = await clipService.GetClipById(clipId, user.DiscordId);
        if (clip is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(clip);
    }

    private static async Task<Results<Ok<ClipShareResponse>, NotFound>> ShareVideo(
        ClipService clipService,
        AuthenticatedUser user,
        Guid clipId)
    {
        ClipShareResponse? response = await clipService.CreateOrGetShare(clipId, user.DiscordId);
        if (response is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(response);
    }

    private static async Task<Results<Ok<Clip>, NotFound>> AddTagToClip(
        ClipService clipService,
        AuthenticatedUser user,
        Guid clipId,
        AddTagRequest request)
    {
        Clip? updated = await clipService.AddTagToClip(clipId, user.DiscordId, request.Tag);
        if (updated is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(updated);
    }

    private static async Task<Results<Ok<Clip>, NotFound>> RemoveTagFromClip(
        ClipService clipService,
        AuthenticatedUser user,
        Guid clipId,
        string tag)
    {
        Clip? updated = await clipService.RemoveTagFromClip(clipId, user.DiscordId, tag);
        if (updated is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(updated);
    }

    private static async Task<Results<Ok<Clip>, NotFound>> UpdateClipTitle(
        ClipService clipService,
        AuthenticatedUser user,
        Guid clipId,
        UpdateTitleRequest request)
    {
        Clip? updated = await clipService.UpdateClipTitle(clipId, user.DiscordId, request.Title);
        if (updated is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(updated);
    }

    private static async Task<Ok<List<TopTag>>> GetTopTags(ClipService clipService)
    {
        return TypedResults.Ok(await clipService.GetTopTags());
    }

    private static async Task<Results<Ok, NotFound>> MarkVideoAsViewed(
        ClipService clipService,
        AuthenticatedUser user,
        Guid clipId)
    {
        bool success = await clipService.MarkClipAsViewed(clipId, user.DiscordId);
        if (!success)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok();
    }

    private static async Task<Results<Ok, NotFound>> DeleteClip(
        ClipService clipService,
        AuthenticatedUser user,
        Guid clipId)
    {
        bool success = await clipService.DeleteClip(clipId, user.DiscordId);
        if (!success)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok();
    }

    private static async Task<Ok<BackfillResult>> BackfillClipMetadata(
        ClipsBackfillService backfillService,
        AuthenticatedUser user)
    {
        BackfillResult result = await backfillService.BackfillClipMetadataAsync();
        return TypedResults.Ok(result);
    }

    public sealed record AddTagRequest(string Tag);

    public sealed record UpdateTitleRequest(string Title);
}

public enum ClipSortOrder
{
    DateDescending,
    DateAscending
}
