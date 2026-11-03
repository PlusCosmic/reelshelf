using Microsoft.AspNetCore.Http.HttpResults;
using Reelshelf.Auth;

namespace Reelshelf.ClipBin;

public static class ClipBinEndpoints
{
    public static void MapClipBinEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("clips/bin")
            .RequireAuthorization();

        group.MapGet("", GetBin).WithName("GetClipBin");
        group.MapPost("", BinClips).WithName("BinClips")
            .RequirePermission(Permissions.ClipsDelete);
        group.MapPost("restore", RestoreClips).WithName("RestoreClips")
            .RequirePermission(Permissions.ClipsDelete);
        group.MapPost("delete", DeleteBinnedClips).WithName("DeleteBinnedClips")
            .RequirePermission(Permissions.ClipsDelete);
    }

    /// <summary>A page of the caller's bin, newest in first.</summary>
    private static async Task<Results<Ok<ClipBinResponse>, BadRequest<string>>> GetBin(
        ClipBinService bin,
        AuthenticatedUser user,
        int page = 1,
        int pageSize = 48)
    {
        if (page < 1 || pageSize is < 1 or > 100)
        {
            return TypedResults.BadRequest("Page must be at least 1 and page size between 1 and 100");
        }

        return TypedResults.Ok(await bin.GetBin(user.Id, page, pageSize));
    }

    /// <summary>Puts clips in the bin: the ids given, or every clip matching the filter. Answers the ones it moved.</summary>
    private static async Task<Ok<BinnedClipsResponse>> BinClips(
        ClipBinService bin,
        AuthenticatedUser user,
        BinClipsRequest request)
    {
        return TypedResults.Ok(await bin.Bin(user.Id, request));
    }

    /// <summary>Takes clips back out of the bin. Answers the ones it restored.</summary>
    private static async Task<Ok<BinnedClipsResponse>> RestoreClips(
        ClipBinService bin,
        AuthenticatedUser user,
        RestoreClipsRequest request)
    {
        return TypedResults.Ok(await bin.Restore(user.Id, request));
    }

    /// <summary>
    /// Deletes binned clips for good: the ids given, or with none the next part of the whole bin (call again while
    /// <c>remaining</c> is above zero and something was deleted).
    /// </summary>
    private static async Task<Ok<DeleteBinnedClipsResponse>> DeleteBinnedClips(
        ClipBinService bin,
        AuthenticatedUser user,
        DeleteBinnedClipsRequest request)
    {
        return TypedResults.Ok(await bin.DeleteForever(user.Id, request));
    }
}
