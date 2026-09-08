using Microsoft.AspNetCore.Http.HttpResults;
using Reelshelf.Auth;
using Reelshelf.Core;
using Reelshelf.Exceptions;

namespace Reelshelf.FFmpeg;

public static class FFmpegEndpoints
{
    public static void MapFFmpegEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("ffmpeg")
            .RequireAuthorization();

        group.MapGet("download/{videoId}", DownloadVideo).WithName("DownloadVideo");
    }

    private static async Task<Results<FileStreamHttpResult, NotFound<string>, ProblemHttpResult>> DownloadVideo(
        FFmpegService ffmpegService,
        ClipsStatements clipsStatements,
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

        try
        {
            FFmpegService.DownloadedVideo download = await ffmpegService.DownloadHlsVideoAsync(videoId, cancellationToken);
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
                fileDownloadName: $"{videoId}.mp4",
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
