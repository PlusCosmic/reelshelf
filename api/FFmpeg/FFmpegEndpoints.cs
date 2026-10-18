using Microsoft.AspNetCore.Http.HttpResults;
using Reelshelf.Auth;
using Reelshelf.Core;
using Reelshelf.Exceptions;
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
    /// A clip whose host keeps it as a file (R2) is a redirect to a short-lived presigned URL that downloads it as an
    /// attachment, with no work here. Anything else (Bunny) is remuxed from its stream into an MP4 on this server.
    /// </summary>
    private static async Task<Results<RedirectHttpResult, FileStreamHttpResult, NotFound<string>, ProblemHttpResult>> DownloadVideo(
        FFmpegService ffmpegService,
        ClipsStatements clipsStatements,
        ClipVideoHosts videoHosts,
        AuthenticatedUser user,
        HttpContext httpContext,
        Guid videoId,
        CancellationToken cancellationToken)
    {
        // Only videos the caller may view can be downloaded; a video id alone is not an entitlement.
        ClipsStatements.ClipRow? clip = await clipsStatements.GetClipByVideoId(videoId);
        if (clip is null || (clip.OwnerId != user.Id && !await clipsStatements.UserCanAccessClip(clip.Id, user.Id)))
        {
            return TypedResults.NotFound("Video not found");
        }

        string fileName = DownloadFileName.For(clip.Title, videoId);
        if (videoHosts.DownloadsFor(clip.VideoHost) is { } downloads)
        {
            if (clip.VideoStatus != ClipVideoStatus.Ready)
            {
                return TypedResults.NotFound("Video is not ready to download");
            }

            // The URL expires within minutes; never let a cache hand out the redirect after that.
            httpContext.Response.Headers.CacheControl = "no-store";
            return TypedResults.Redirect(downloads.DownloadUrl(clip.ToVideoRef(), fileName));
        }

        try
        {
            FFmpegService.DownloadedVideo download = await ffmpegService.DownloadHlsVideoAsync(clip.ToVideoRef(), cancellationToken);
            FileStream fileStream;
            try
            {
                fileStream = new FileStream(download.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.DeleteOnClose);
            }
            catch
            {
                download.Dispose();
                throw;
            }

            // Keep the download permit until the response has been written, so the number of finished files
            // waiting on slow clients is bounded by MaxConcurrentDownloads too.
            httpContext.Response.RegisterForDispose(download);

            return TypedResults.File(
                fileStream,
                contentType: "video/mp4",
                fileDownloadName: fileName,
                enableRangeProcessing: true);
        }
        catch (FileNotFoundException ex)
        {
            return TypedResults.NotFound(ex.Message);
        }
        catch (ServiceUnavailableException ex)
        {
            return TypedResults.Problem(
                detail: ex.Message,
                title: "Downloads busy",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (Exception ex)
        {
            return TypedResults.Problem(
                detail: ex.Message,
                title: "Failed to download video",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
