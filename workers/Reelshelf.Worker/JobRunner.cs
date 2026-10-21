using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Reelshelf.Worker;

/// <summary>
/// Runs one job in its own work directory: fetches the source, makes the outputs, uploads them and returns what to
/// report on completion. Every job uploads its six frames first and reports them ready before anything slower.
/// Outputs go to versioned keys through presigned PUTs, so running a job again overwrites them safely.
/// </summary>
public sealed class JobRunner(
    FfmpegRunner ffmpeg,
    MediaTransfer transfer,
    HlsRemux hlsRemux,
    IOptions<WorkerSettings> options,
    ILogger<JobRunner> logger)
{
    private readonly WorkerSettings settings = options.Value;

    public async Task<JobResult> RunAsync(
        WorkerJob job,
        string workDirectory,
        FfmpegCapabilities capabilities,
        JobReporter reporter,
        CancellationToken cancellationToken)
    {
        if (job.Type is not (JobTypes.Transcode or JobTypes.Migrate or JobTypes.Thumbnails or JobTypes.Trim))
        {
            throw new JobFailedException($"This worker doesn't run {job.Type} jobs", retryable: false);
        }

        if (string.IsNullOrEmpty(job.SourceUrl) || job.UploadUrls is null)
        {
            throw new JobFailedException("The job has no presigned source or upload URLs", retryable: false);
        }

        JobInput input = JobInput.Parse(job.Input);
        string source = Path.Combine(workDirectory, "source");
        // A migrate's source is an HLS playlist, copied into a local MP4; from there it is a transcode.
        long sourceBytes = job.Type == JobTypes.Migrate
            ? await hlsRemux.RemuxAsync(job.SourceUrl, source, cancellationToken)
            : await transfer.DownloadAsync(job.SourceUrl, source, settings.MaxSourceBytes, cancellationToken);
        MediaInfo media = await ProbeInputAsync(source, cancellationToken);
        logger.LogInformation(
            "Job {RunId}: {Type} of a {Duration:0.##}s {Width}x{Height} {Codec} source ({Bytes} bytes), encoding at {FrameRate} fps",
            job.RunId, job.Type, media.DurationSeconds, media.Video!.Width, media.Video.Height, media.Video.Codec, sourceBytes,
            media.Video.TargetFrameRate);

        return job.Type switch
        {
            JobTypes.Transcode or JobTypes.Migrate => await TranscodeAsync(job, input, source, media, capabilities, reporter, workDirectory, cancellationToken),
            JobTypes.Thumbnails => await ThumbnailsAsync(job, source, media, sourceBytes, reporter, workDirectory, cancellationToken),
            _ => await TrimAsync(job, input, source, media, reporter, workDirectory, cancellationToken)
        };
    }

    private async Task<JobResult> TranscodeAsync(
        WorkerJob job,
        JobInput input,
        string source,
        MediaInfo media,
        FfmpegCapabilities capabilities,
        JobReporter reporter,
        string workDirectory,
        CancellationToken cancellationToken)
    {
        bool toneMap = media.Video!.IsHdr;
        if (toneMap && !capabilities.CanToneMap)
        {
            throw new JobFailedException("The source is HDR and this worker's ffmpeg can't tone-map it (no zscale)",
                retryable: false);
        }

        TrimRange trim = input.Resolve(media.DurationSeconds);
        List<string> files = await FramesAsync(job, source, trim, toneMap, reporter, workDirectory, cancellationToken);

        string video = Path.Combine(workDirectory, OutputFiles.Video);
        reporter.EncodeStarting();
        await ffmpeg.RunFfmpegAsync(EncodeArguments.Transcode(source, video, settings, trim, toneMap, media.Video.TargetFrameRate),
            cancellationToken,
            trim.LengthSeconds, share => reporter.Report((int)(share * 100)));

        if (job.UploadUrls!.TryGetValue(OutputFiles.Preview, out string? previewUrl))
        {
            string preview = Path.Combine(workDirectory, OutputFiles.Preview);
            double start = trim.StartSeconds + FrameTimings.PreviewStart(trim.LengthSeconds, EncodeArguments.PreviewSeconds);
            await ffmpeg.RunFfmpegAsync(
                EncodeArguments.Preview(source, preview, start, trim.LengthSeconds, toneMap), cancellationToken);
            await transfer.UploadAsync(previewUrl, preview, OutputFiles.ContentType(OutputFiles.Preview), cancellationToken);
            files.Add(OutputFiles.Preview);
        }

        return await FinishVideoAsync(job, video, files, toneMap ? true : null, cancellationToken);
    }

    /// <summary>New frames from an existing output, which stays as it is.</summary>
    private async Task<JobResult> ThumbnailsAsync(
        WorkerJob job,
        string source,
        MediaInfo media,
        long sourceBytes,
        JobReporter reporter,
        string workDirectory,
        CancellationToken cancellationToken)
    {
        TrimRange whole = new(0, media.DurationSeconds, false);
        List<string> files = await FramesAsync(job, source, whole, toneMap: false, reporter, workDirectory, cancellationToken);
        return new JobResult(media.DurationSeconds, sourceBytes,
            new JobOutputs(Codecs.For(media), media.Video!.Width, media.Video.Height, files));
    }

    /// <summary>
    /// A shorter version of the current output: a stream copy, which snaps the start to a keyframe, or a re-encode
    /// of the range when the job asks for frame accuracy. Frames come from the new version.
    /// </summary>
    private async Task<JobResult> TrimAsync(
        WorkerJob job,
        JobInput input,
        string source,
        MediaInfo media,
        JobReporter reporter,
        string workDirectory,
        CancellationToken cancellationToken)
    {
        if (!input.HasTrim)
        {
            throw new JobFailedException("A trim job needs trim_start_seconds or trim_end_seconds", retryable: false);
        }

        TrimRange trim = input.Resolve(media.DurationSeconds);
        string video = Path.Combine(workDirectory, OutputFiles.Video);
        IReadOnlyList<string> arguments = input.FrameAccurate
            ? EncodeArguments.Transcode(source, video, settings, trim, toneMap: false, media.Video!.TargetFrameRate)
            : EncodeArguments.TrimCopy(source, video, trim);
        reporter.EncodeStarting();
        await ffmpeg.RunFfmpegAsync(arguments, cancellationToken, trim.LengthSeconds,
            share => reporter.Report((int)(share * 100)));

        MediaInfo trimmed = await ffmpeg.ProbeAsync(video, cancellationToken);
        TrimRange whole = new(0, trimmed.DurationSeconds, false);
        List<string> files = await FramesAsync(job, video, whole, toneMap: false, reporter, workDirectory, cancellationToken);
        return await FinishVideoAsync(job, video, files, null, cancellationToken);
    }

    /// <summary>Uploads <c>av1.mp4</c> and reads back what was produced for the completion report.</summary>
    private async Task<JobResult> FinishVideoAsync(
        WorkerJob job,
        string video,
        List<string> files,
        bool? toneMapped,
        CancellationToken cancellationToken)
    {
        MediaInfo output = await ffmpeg.ProbeAsync(video, cancellationToken);
        if (output.Video is null || output.DurationSeconds <= 0)
        {
            throw new JobFailedException("The encode produced no playable video", retryable: true);
        }

        string url = job.UploadUrls!.TryGetValue(OutputFiles.Video, out string? videoUrl)
            ? videoUrl
            : throw new JobFailedException($"The job has no upload URL for {OutputFiles.Video}", retryable: false);
        await transfer.UploadAsync(url, video, OutputFiles.ContentType(OutputFiles.Video), cancellationToken);
        files.Insert(0, OutputFiles.Video);

        long size = new FileInfo(video).Length;
        string codecs = Codecs.For(output);
        logger.LogInformation("Job {RunId}: produced {Width}x{Height} {Codecs}, {Duration:0.##}s, {Size} bytes",
            job.RunId, output.Video.Width, output.Video.Height, codecs, output.DurationSeconds, size);
        return new JobResult(output.DurationSeconds, size,
            new JobOutputs(codecs, output.Video.Width, output.Video.Height, files, toneMapped));
    }

    /// <summary>
    /// The six frames of the kept range, uploaded, then reported ready. A frame that comes out empty (a seek past
    /// the last decodable frame of a very short or damaged clip) is taken from the range's start instead.
    /// </summary>
    private async Task<List<string>> FramesAsync(
        WorkerJob job,
        string input,
        TrimRange range,
        bool toneMap,
        JobReporter reporter,
        string workDirectory,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<double> times = FrameTimings.For(range.LengthSeconds);
        List<string> files = [];
        for (int index = 0; index < OutputFiles.Frames.Count; index++)
        {
            string name = OutputFiles.Frames[index];
            if (!job.UploadUrls!.TryGetValue(name, out string? url))
            {
                throw new JobFailedException($"The job has no upload URL for {name}", retryable: false);
            }

            string path = Path.Combine(workDirectory, name);
            await ffmpeg.RunFfmpegAsync(
                EncodeArguments.Frame(input, path, range.StartSeconds + times[index], toneMap), cancellationToken);
            if (!HasContent(path))
            {
                await ffmpeg.RunFfmpegAsync(EncodeArguments.Frame(input, path, range.StartSeconds, toneMap), cancellationToken);
                if (!HasContent(path))
                {
                    throw new JobFailedException("No frame could be decoded from the video", retryable: false);
                }
            }

            await transfer.UploadAsync(url, path, OutputFiles.ContentType(name), cancellationToken);
            files.Add(name);
        }

        await reporter.ThumbnailsReadyAsync(cancellationToken);
        return files;
    }

    /// <summary>Probes the downloaded input; one ffprobe can't read, or without video, is bad input.</summary>
    private async Task<MediaInfo> ProbeInputAsync(string source, CancellationToken cancellationToken)
    {
        MediaInfo media;
        try
        {
            media = await ffmpeg.ProbeAsync(source, cancellationToken);
        }
        catch (FfmpegException exception)
        {
            throw new JobFailedException($"The source isn't a readable video: {exception.Message}", retryable: false, exception);
        }

        if (media.Video is null || media.Video.Width <= 0 || media.Video.Height <= 0)
        {
            throw new JobFailedException("The source has no video stream", retryable: false);
        }

        if (media.DurationSeconds <= 0)
        {
            throw new JobFailedException("The source's duration is unknown", retryable: false);
        }

        return media;
    }

    private static bool HasContent(string path) => File.Exists(path) && new FileInfo(path).Length > 0;
}
