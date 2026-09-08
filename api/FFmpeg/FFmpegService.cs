using FFMpegCore;
using Reelshelf.Exceptions;

namespace Reelshelf.FFmpeg;

public class FFmpegService
{
    private const int DefaultMaxConcurrentDownloads = 3;
    private static readonly TimeSpan QueueWait = TimeSpan.FromSeconds(30);

    // Process-wide limits (the service itself is scoped). Each download spawns an ffmpeg process, so
    // a single account must not be able to fan out unboundedly now that sign-up is open.
    private static SemaphoreSlim? _globalDownloads;
    private static readonly object GlobalDownloadsInit = new();
    // Per-video gates are reference counted and evicted once the last holder or waiter leaves, so the
    // dictionary tracks in-flight videos only rather than every video ever downloaded.
    private static readonly Dictionary<Guid, VideoGate> PerVideoGates = new();

    private sealed class VideoGate
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int RefCount;
    }

    private readonly ILogger<FFmpegService> _logger;
    private readonly string _outputPath;

    public FFmpegService(ILogger<FFmpegService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _outputPath = configuration["FFmpegOutputPath"] ?? Path.Combine(Path.GetTempPath(), "ffmpeg-downloads");

        int maxConcurrent = configuration.GetValue<int?>("FFmpeg:MaxConcurrentDownloads") ?? DefaultMaxConcurrentDownloads;
        lock (GlobalDownloadsInit)
        {
            _globalDownloads ??= new SemaphoreSlim(Math.Max(1, maxConcurrent), Math.Max(1, maxConcurrent));
        }

        // Ensure output directory exists
        Directory.CreateDirectory(_outputPath);
    }

    /// <summary>
    /// Downloads a video from an HLS playlist URL and returns the file path
    /// </summary>
    /// <param name="videoId">The Bunny video ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Path to the downloaded video file</returns>
    public async Task<DownloadedVideo> DownloadHlsVideoAsync(Guid videoId, CancellationToken cancellationToken = default)
    {
        // Take the per-video gate first: duplicate requests for one video queue here without holding
        // any process-wide capacity, so they cannot starve downloads of other videos.
        VideoGate gate = RentGate(videoId);
        try
        {
            if (!await gate.Semaphore.WaitAsync(QueueWait, cancellationToken))
            {
                throw new ServiceUnavailableException("This clip is already being downloaded. Try again in a moment.");
            }

            try
            {
                SemaphoreSlim globalDownloads = _globalDownloads!;
                if (!await globalDownloads.WaitAsync(QueueWait, cancellationToken))
                {
                    throw new ServiceUnavailableException("Too many downloads are in progress. Try again in a moment.");
                }

                try
                {
                    string path = await DownloadHlsVideoCoreAsync(videoId, cancellationToken);
                    // The global permit is handed to the result and released when the caller disposes it,
                    // i.e. once the response has been streamed. Otherwise a slow client could keep many
                    // finished MP4s open on disk while new conversions start, defeating the cap.
                    return new DownloadedVideo(path, globalDownloads);
                }
                catch
                {
                    globalDownloads.Release();
                    throw;
                }
            }
            finally
            {
                // Every exit after the video gate was taken (timeout, cancellation, ffmpeg failure) releases it.
                gate.Semaphore.Release();
            }
        }
        finally
        {
            ReturnGate(videoId, gate);
        }
    }

    /// <summary>
    /// A converted file on disk plus the download permit it occupies. Dispose after the file has been
    /// streamed (or could not be opened) to return that capacity.
    /// </summary>
    public sealed class DownloadedVideo : IDisposable
    {
        private SemaphoreSlim? _capacity;

        internal DownloadedVideo(string path, SemaphoreSlim capacity)
        {
            Path = path;
            _capacity = capacity;
        }

        public string Path { get; }

        public void Dispose()
        {
            Interlocked.Exchange(ref _capacity, null)?.Release();
        }
    }

    private static VideoGate RentGate(Guid videoId)
    {
        lock (PerVideoGates)
        {
            if (!PerVideoGates.TryGetValue(videoId, out VideoGate? gate))
            {
                gate = new VideoGate();
                PerVideoGates[videoId] = gate;
            }

            gate.RefCount++;
            return gate;
        }
    }

    private static void ReturnGate(Guid videoId, VideoGate gate)
    {
        lock (PerVideoGates)
        {
            if (--gate.RefCount > 0)
            {
                return;
            }

            // Nobody holds or waits on this gate any more, so it can leave the map and be disposed. A later
            // request for the same video rents a fresh gate.
            PerVideoGates.Remove(videoId);
            gate.Semaphore.Dispose();
        }
    }

    private async Task<string> DownloadHlsVideoCoreAsync(Guid videoId, CancellationToken cancellationToken)
    {
        string hlsUrl = $"https://vz-cd8f9809-39a.b-cdn.net/{videoId}/playlist.m3u8";
        // Unique per request: the endpoint streams the file with DeleteOnClose, so two requests for the
        // same video must never share a path.
        string outputFileName = $"{videoId}-{Guid.NewGuid():N}.mp4";
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
