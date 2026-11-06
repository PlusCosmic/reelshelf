using Microsoft.AspNetCore.Http.HttpResults;
using Reelshelf.Auth;
using Reelshelf.Core;
using Reelshelf.Video;

namespace Reelshelf.FFmpeg;

public static class FFmpegEndpoints
{
    public static void MapFFmpegEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("ffmpeg")
            .RequireAuthorization();

        group.MapGet("download/{videoId}", DownloadVideo).WithName("DownloadVideo");
    }

    /// <summary>
    /// A redirect to a short-lived presigned URL that downloads the clip's file as an attachment, with no work here.
    /// </summary>
    private static async Task<Results<RedirectHttpResult, NotFound<string>>> DownloadVideo(
        ClipsStatements clipsStatements,
        ClipVideoHosts videoHosts,
        AuthenticatedUser user,
        HttpContext httpContext,
        Guid videoId)
    {
        // Only videos the caller may view can be downloaded; a video id alone is not an entitlement.
        ClipsStatements.ClipRow? clip = await clipsStatements.GetClipByVideoId(videoId);
        if (clip is null || (clip.OwnerId != user.Id && !await clipsStatements.UserCanAccessClip(clip.Id, user.Id)))
        {
            return TypedResults.NotFound("Video not found");
        }

        if (videoHosts.DownloadsFor(clip.VideoHost) is not { } downloads)
        {
            return TypedResults.NotFound("Video not found");
        }

        if (clip.VideoStatus != ClipVideoStatus.Ready)
        {
            return TypedResults.NotFound("Video is not ready to download");
        }

        // The URL expires within minutes; never let a cache hand out the redirect after that.
        httpContext.Response.Headers.CacheControl = "no-store";
        return TypedResults.Redirect(downloads.DownloadUrl(clip.ToVideoRef(), DownloadFileName.For(clip.Title, videoId)));
    }
}
