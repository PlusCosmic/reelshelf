using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Reelshelf.Auth;
using Reelshelf.Core;
using Reelshelf.Exceptions;

namespace Reelshelf.Video;

/// <summary>
/// The browser's side of a presigned multipart upload (<see cref="UploadTarget.S3Multipart"/>), under the path the
/// upload target names: part URLs in batches, the parts uploaded so far (to resume after a reload) and completion,
/// which checks the file and queues its transcode. Only the clip's owner may use them, and only for a clip whose host
/// takes multipart uploads; any other clip is not found. Cancelling is deleting the clip, which aborts the upload.
/// </summary>
public static class MultipartUploadEndpoints
{
    /// <summary>The most part URLs one request may ask for.</summary>
    public const int MaxPartsPerBatch = 100;

    /// <summary>Where a clip's upload lives in the API, as the upload target's endpoint.</summary>
    public static string UploadPath(Guid clipId) => $"/api/clips/videos/{clipId}/upload";

    public static void MapMultipartUploadEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("clips/videos/{clipId:guid}/upload");

        group.MapPost("parts", PresignUploadParts).WithName("PresignUploadParts")
            .RequireAuthorization()
            .RequirePermission(Permissions.ClipsCreate);
        group.MapGet("parts", ListUploadParts).WithName("ListUploadParts")
            .RequireAuthorization()
            .RequirePermission(Permissions.ClipsCreate);
        // Completing runs ffprobe, so it shares preparation's budget.
        group.MapPost("complete", CompleteUpload).WithName("CompleteUpload")
            .RequireAuthorization()
            .RequirePermission(Permissions.ClipsCreate)
            .RequireRateLimiting(RateLimitPolicies.ClipPrepare);
    }

    private static async Task<Ok<PresignedUploadParts>> PresignUploadParts(
        ClipsStatements clipsStatements,
        ClipVideoHosts videoHosts,
        AuthenticatedUser user,
        Guid clipId,
        PresignUploadPartsRequest request)
    {
        (ClipsStatements.ClipRow clip, IMultipartUploadHost host) = await FindAsync(clipsStatements, videoHosts, clipId, user.Id);
        return TypedResults.Ok(host.PresignParts(clip, request.UploadId, request.PartNumbers ?? []));
    }

    private static async Task<Ok<UploadedParts>> ListUploadParts(
        ClipsStatements clipsStatements,
        ClipVideoHosts videoHosts,
        AuthenticatedUser user,
        Guid clipId,
        [FromQuery] string uploadId,
        CancellationToken cancellationToken)
    {
        (ClipsStatements.ClipRow clip, IMultipartUploadHost host) = await FindAsync(clipsStatements, videoHosts, clipId, user.Id);
        return TypedResults.Ok(new UploadedParts(await host.ListPartsAsync(clip, uploadId, cancellationToken)));
    }

    private static async Task<Ok<CompletedUpload>> CompleteUpload(
        ClipsStatements clipsStatements,
        ClipVideoHosts videoHosts,
        AuthenticatedUser user,
        Guid clipId,
        CompleteUploadRequest request,
        CancellationToken cancellationToken)
    {
        (ClipsStatements.ClipRow clip, IMultipartUploadHost host) = await FindAsync(clipsStatements, videoHosts, clipId, user.Id);
        return TypedResults.Ok(await host.CompleteUploadAsync(clip, request.UploadId, request.Parts ?? [], cancellationToken));
    }

    /// <summary>The caller's clip and its host, when that host takes multipart uploads; otherwise 404.</summary>
    internal static async Task<(ClipsStatements.ClipRow Clip, IMultipartUploadHost Host)> FindAsync(
        ClipsStatements clipsStatements, ClipVideoHosts videoHosts, Guid clipId, Guid ownerId)
    {
        ClipsStatements.ClipRow? clip = await clipsStatements.GetClipById(clipId);
        if (clip is null || clip.OwnerId != ownerId || videoHosts.For(clip.VideoHost) is not IMultipartUploadHost host)
        {
            throw new NotFoundException("Clip", clipId);
        }

        return (clip, host);
    }
}
