using FFMpegCore;

namespace Nucleus.Clips.FFmpeg;

public class FFmpegService
{
    private readonly ILogger<FFmpegService> _logger;
    private readonly string _outputPath;

    public FFmpegService(ILogger<FFmpegService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _outputPath = configuration["FFmpegOutputPath"] ?? Path.Combine(Path.GetTempPath(), "ffmpeg-downloads");

        // Ensure output directory exists
        Directory.CreateDirectory(_outputPath);
    }

    /// <summary>
    /// Downloads a video from an HLS playlist URL and returns the file path
    /// </summary>
    /// <param name="videoId">The Bunny video ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Path to the downloaded video file</returns>
    public async Task<string> DownloadHlsVideoAsync(Guid videoId, CancellationToken cancellationToken = default)
    {
        string hlsUrl = $"https://vz-cd8f9809-39a.b-cdn.net/{videoId}/playlist.m3u8";
        string outputFileName = $"{videoId}.mp4";
        string outputPath = Path.Combine(_outputPath, outputFileName);

        try
        {
            _logger.LogInformation("Starting HLS download for video {VideoId} from {HlsUrl}", videoId, hlsUrl);

            // Use FFMpegCore to download and convert the HLS stream
            // Using -c copy to avoid re-encoding (stream copy)
            // The -bsf:a aac_adtstoasc bitstream filter is applied automatically when needed
            await FFMpegArguments
                .FromUrlInput(new Uri(hlsUrl))
                .OutputToFile(outputPath, overwrite: true, options => options
                    .CopyChannel() // Equivalent to -c copy (no re-encoding)
                    .WithCustomArgument("-bsf:a aac_adtstoasc")) // AAC bitstream filter
                .CancellableThrough(cancellationToken)
                .ProcessAsynchronously();

            if (!File.Exists(outputPath))
            {
                _logger.LogError("FFmpeg completed but output file not found at {OutputPath}", outputPath);
                throw new FileNotFoundException("Output file was not created", outputPath);
            }

            _logger.LogInformation("Successfully downloaded video {VideoId} to {OutputPath}", videoId, outputPath);
            return outputPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading HLS video {VideoId}", videoId);

            // Clean up partial file if it exists
            if (File.Exists(outputPath))
            {
                try
                {
                    File.Delete(outputPath);
                }
                catch (Exception deleteEx)
                {
                    _logger.LogWarning(deleteEx, "Failed to delete partial file {OutputPath}", outputPath);
                }
            }

            throw;
        }
    }

    /// <summary>
    /// Deletes a downloaded video file
    /// </summary>
    /// <param name="filePath">Path to the file to delete</param>
    public void DeleteDownloadedVideo(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _logger.LogInformation("Deleted video file {FilePath}", filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete video file {FilePath}", filePath);
        }
    }
}
