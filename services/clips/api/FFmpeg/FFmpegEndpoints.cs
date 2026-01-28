using Microsoft.AspNetCore.Http.HttpResults;
using Nucleus.Shared.Auth;

namespace Nucleus.Clips.FFmpeg;

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
        AuthenticatedUser user,
        Guid videoId,
        CancellationToken cancellationToken)
    {
        try
        {
            string filePath = await ffmpegService.DownloadHlsVideoAsync(videoId, cancellationToken);
            var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.DeleteOnClose);

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
        catch (Exception ex)
        {
            return TypedResults.Problem(
                detail: ex.Message,
                title: "Failed to download video",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
