using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Reelshelf.Worker.Test;

/// <summary>
/// The worker end to end against <see cref="FakeWorkerApi"/>: claim, download, encode with the real ffmpeg on clips
/// generated with <c>lavfi</c>, upload, progress and completion.
/// </summary>
public sealed partial class WorkerLoopTests : IAsyncLifetime
{
    private static readonly string[] TranscodeOutputs =
        [OutputFiles.Video, .. OutputFiles.Frames, OutputFiles.Preview];

    private static readonly string[] TrimOutputs = [OutputFiles.Video, .. OutputFiles.Frames];

    private readonly string root = Path.Combine(Path.GetTempPath(), "reelshelf-worker-test-" + Guid.NewGuid().ToString("N"));
    private FakeWorkerApi api = null!;
    private ServiceProvider services = null!;

    private string WorkDirectory => Path.Combine(root, "work");

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(root);
        api = new FakeWorkerApi(Path.Combine(root, "storage"));
        services = BuildServices();
        return Task.CompletedTask;
    }

    /// <summary>The worker's services against the fake API, with <paramref name="overrides"/> on top of the test settings.</summary>
    private ServiceProvider BuildServices(Dictionary<string, string?>? overrides = null)
    {
        Dictionary<string, string?> settings = new()
        {
            ["Worker:ApiBaseUrl"] = api.BaseUrl,
            ["Worker:Secret"] = FakeWorkerApi.Secret,
            ["Worker:Preset"] = "12",
            ["Worker:LogicalProcessors"] = "2",
            ["Worker:TargetBitrateKbps"] = "1000",
            ["Worker:Niceness"] = "5",
            ["Worker:WorkDirectory"] = WorkDirectory,
            ["Worker:ShutdownGrace"] = "00:00:00.200"
        };
        foreach ((string key, string? value) in overrides ?? [])
        {
            settings[key] = value;
        }

        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        ServiceCollection collection = new();
        collection.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Debug));
        collection.AddTranscodeWorker(configuration);
        return collection.BuildServiceProvider();
    }

    /// <summary>
    /// Runs the worker with ffmpeg replaced by a script that runs <paramref name="before"/> (sh) when its arguments
    /// contain <paramref name="match"/>, then the real ffmpeg, and with the given shutdown grace.
    /// </summary>
    private async Task UseFfmpegWrapperAsync(string match, string before, TimeSpan shutdownGrace)
    {
        string script = Path.Combine(root, "ffmpeg-wrapper.sh");
        await File.WriteAllTextAsync(script, $$"""
            #!/bin/sh
            case " $* " in
              *" {{match}} "*) {{before}} ;;
            esac
            exec ffmpeg "$@"

            """);
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        await services.DisposeAsync();
        services = BuildServices(new Dictionary<string, string?>
        {
            ["Worker:FfmpegPath"] = script,
            ["Worker:ShutdownGrace"] = shutdownGrace.ToString("c")
        });
    }

    public async Task DisposeAsync()
    {
        await services.DisposeAsync();
        await api.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }

    [SvtAv1Fact]
    public async Task Transcode_EncodesTheTrimmedSource_UploadsFramesFirst_AndCompletes()
    {
        string source = await GenerateAsync("source.mp4", "testsrc2=size=1280x720:rate=60:duration=4", audio: true,
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac");
        api.Enqueue(JobTypes.Transcode, source, TranscodeOutputs,
            """{"source_key": "uploads/x", "trim_start_seconds": 0.5, "trim_end_seconds": 3.5}""");

        Assert.True(await RunOnceAsync());

        Assert.Null(api.Failure);
        JsonElement completion = Assert.IsType<JsonElement>(api.Completion);
        Assert.InRange(completion.GetProperty("length_seconds").GetDouble(), 2.9, 3.1);
        Assert.Equal(api.Uploads[OutputFiles.Video].Length, completion.GetProperty("size_bytes").GetInt64());
        JsonElement outputs = completion.GetProperty("outputs");
        Assert.Matches(Av1WithOpus(), outputs.GetProperty("codecs").GetString());
        Assert.Equal(1280, outputs.GetProperty("width").GetInt32());
        Assert.Equal(720, outputs.GetProperty("height").GetInt32());
        Assert.Equal(TranscodeOutputs.Order(), outputs.GetProperty("files").EnumerateArray().Select(file => file.GetString()).Order());

        AssertUploadHeaders(TranscodeOutputs);
        AssertFramesReportedAfterUpload();

        MediaInfo video = await ProbeAsync(api.Uploads[OutputFiles.Video].Path);
        Assert.Equal("av1", video.Video!.Codec);
        Assert.Equal("opus", video.Audio!.Codec);
        Assert.Equal(outputs.GetProperty("codecs").GetString(), Codecs.For(video));
        Assert.Equal("30/1", await FrameRateAsync(api.Uploads[OutputFiles.Video].Path));

        MediaInfo preview = await ProbeAsync(api.Uploads[OutputFiles.Preview].Path);
        Assert.Equal("av1", preview.Video!.Codec);
        Assert.Null(preview.Audio);
        Assert.Equal(640, preview.Video.Width);
        Assert.InRange(preview.DurationSeconds, 2.5, 3.2);

        MediaInfo thumbnail = await ProbeAsync(api.Uploads[OutputFiles.Thumbnail].Path);
        Assert.Equal(1280, thumbnail.Video!.Width);

        Assert.Empty(Directory.EnumerateFileSystemEntries(WorkDirectory));
        Assert.Equal(0, api.Unauthorized);
    }

    [SvtAv1Fact]
    public async Task Transcode_FitsLargeSourcesInside1080p_AndToneMapsHdr()
    {
        // 10-bit, tagged PQ/BT.2020 like an HDR10 capture.
        string source = await GenerateAsync("hdr.mkv", "testsrc2=size=2560x1440:rate=30:duration=1", audio: false,
            "-vf", "format=yuv420p10le", "-c:v", "libsvtav1", "-preset", "12",
            "-color_trc", "smpte2084", "-color_primaries", "bt2020", "-colorspace", "bt2020nc");
        api.Enqueue(JobTypes.Transcode, source, TranscodeOutputs);

        Assert.True(await RunOnceAsync());

        Assert.Null(api.Failure);
        JsonElement outputs = api.Completion!.Value.GetProperty("outputs");
        Assert.Equal(1920, outputs.GetProperty("width").GetInt32());
        Assert.Equal(1080, outputs.GetProperty("height").GetInt32());
        Assert.True(outputs.GetProperty("tone_mapped").GetBoolean());
        // No audio in the source, so none in the codecs.
        Assert.Matches(@"^av01\.0\.\d\dM\.08$", outputs.GetProperty("codecs").GetString());

        MediaInfo video = await ProbeAsync(api.Uploads[OutputFiles.Video].Path);
        Assert.Equal("bt709", video.Video!.ColorTransfer);
        Assert.Equal(8, video.Video.BitDepth);
        Assert.Equal(1920, (await ProbeAsync(api.Uploads[OutputFiles.Thumbnail].Path)).Video!.Width);
    }

    [SvtAv1Fact]
    public async Task Trim_StreamCopiesTheOutput_AndTakesNewFrames()
    {
        string source = await Av1SourceAsync();
        api.Enqueue(JobTypes.Trim, source, TrimOutputs, """{"trim_start_seconds": 1, "trim_end_seconds": 3}""");

        Assert.True(await RunOnceAsync());

        Assert.Null(api.Failure);
        JsonElement completion = api.Completion!.Value;
        // The cut starts on a keyframe, at most a second early.
        Assert.InRange(completion.GetProperty("length_seconds").GetDouble(), 1.9, 3.1);
        Assert.Matches(Av1WithOpus(), completion.GetProperty("outputs").GetProperty("codecs").GetString());
        Assert.Equal(TrimOutputs.Order(), api.Uploads.Keys.Order());
        AssertUploadHeaders(TrimOutputs);
        AssertFramesReportedAfterUpload();
    }

    [SvtAv1Fact]
    public async Task FrameAccurateTrim_ReencodesExactlyTheRange()
    {
        string source = await Av1SourceAsync();
        api.Enqueue(JobTypes.Trim, source, TrimOutputs,
            """{"trim_start_seconds": 1.5, "trim_end_seconds": 3, "frame_accurate": true}""");

        Assert.True(await RunOnceAsync());

        Assert.Null(api.Failure);
        Assert.InRange(api.Completion!.Value.GetProperty("length_seconds").GetDouble(), 1.4, 1.6);
    }

    [SvtAv1Fact]
    public async Task Thumbnails_RegeneratesTheFrames_AndLeavesTheVideo()
    {
        string source = await Av1SourceAsync();
        api.Enqueue(JobTypes.Thumbnails, source, OutputFiles.Frames);

        Assert.True(await RunOnceAsync());

        Assert.Null(api.Failure);
        JsonElement completion = api.Completion!.Value;
        Assert.Equal(new FileInfo(source).Length, completion.GetProperty("size_bytes").GetInt64());
        Assert.Equal(OutputFiles.Frames.Order(), api.Uploads.Keys.Order());
        Assert.Equal(OutputFiles.Frames, completion.GetProperty("outputs").GetProperty("files").EnumerateArray()
            .Select(file => file.GetString()!));
        AssertFramesReportedAfterUpload();
    }

    [SvtAv1Fact]
    public async Task ALostLease_DropsTheJob_WithoutReporting()
    {
        api.LoseLeaseOnProgress = true;
        string source = await Av1SourceAsync();
        api.Enqueue(JobTypes.Transcode, source, TranscodeOutputs);

        Assert.True(await RunOnceAsync());

        Assert.Null(api.Completion);
        Assert.Null(api.Failure);
        Assert.False(api.Uploads.ContainsKey(OutputFiles.Video));
        Assert.Empty(Directory.EnumerateFileSystemEntries(WorkDirectory));
    }

    [SvtAv1Fact]
    public async Task Shutdown_StopsAnEncodePastTheGrace_AndReleasesTheJob()
    {
        string source = await GenerateAsync("long.mp4", "testsrc2=size=1280x720:rate=30:duration=60", audio: false,
            "-c:v", "libx264", "-preset", "ultrafast");
        WorkerJob job = api.Enqueue(JobTypes.Transcode, source, TranscodeOutputs);

        WorkerLoop loop = services.GetRequiredService<WorkerLoop>();
        await loop.StartAsync();
        using CancellationTokenSource stopping = new();
        Task<bool> run = loop.RunOnceAsync(stopping.Token);
        // Shut down once the encode is under way.
        while (api.Progress.IsEmpty && !run.IsCompleted)
        {
            await Task.Delay(50);
        }

        await stopping.CancelAsync();
        Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(30)));

        // Handed back, not failed, so the job keeps its attempts.
        Assert.Null(api.Completion);
        Assert.Null(api.Failure);
        JsonElement release = Assert.IsType<JsonElement>(api.Release);
        Assert.Equal(job.LeaseId, release.GetProperty("lease_id").GetGuid());
        Assert.Equal("release", api.Events.Last());
        Assert.Empty(Directory.EnumerateFileSystemEntries(WorkDirectory));
    }

    [SvtAv1Fact]
    public async Task Shutdown_BeforeTheEncode_ReleasesTheJobAtOnce()
    {
        // Frames take 30 s and the grace is a minute: only an immediate release finishes quickly.
        await UseFfmpegWrapperAsync("-frames:v", "sleep 30", TimeSpan.FromMinutes(1));
        string source = await Av1SourceAsync();
        api.Enqueue(JobTypes.Transcode, source, TranscodeOutputs);

        WorkerLoop loop = services.GetRequiredService<WorkerLoop>();
        await loop.StartAsync();
        using CancellationTokenSource stopping = new();
        Task<bool> run = loop.RunOnceAsync(stopping.Token);
        await Task.Delay(TimeSpan.FromSeconds(1));
        Stopwatch elapsed = Stopwatch.StartNew();
        await stopping.CancelAsync();
        Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(20)));

        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(15), $"The release took {elapsed.Elapsed}");
        Assert.Null(api.Failure);
        Assert.NotNull(api.Release);
        Assert.False(api.Uploads.ContainsKey(OutputFiles.Video));
    }

    [SvtAv1Fact]
    public async Task ACrashingEncode_FailsAsRetryable_AndIsNotReleased()
    {
        string started = Path.Combine(root, "encode-started");
        await UseFfmpegWrapperAsync("-progress", $"touch '{started}'; kill -SEGV $$", TimeSpan.FromMinutes(1));
        string source = await Av1SourceAsync();
        api.Enqueue(JobTypes.Transcode, source, TranscodeOutputs);

        Assert.True(await RunOnceAsync());

        Assert.True(File.Exists(started));
        Assert.Null(api.Completion);
        Assert.Null(api.Release);
        JsonElement failure = Assert.IsType<JsonElement>(api.Failure);
        Assert.True(failure.GetProperty("retryable").GetBoolean());
        Assert.Contains("SIGSEGV", failure.GetProperty("error").GetString());
    }

    [SvtAv1Fact]
    public async Task ACrashDuringTheShutdownGrace_StillFailsTheJob()
    {
        // The encode crashes a second after it starts, while the shutdown's minute of grace is still running.
        string started = Path.Combine(root, "encode-started");
        await UseFfmpegWrapperAsync("-progress", $"touch '{started}'; sleep 1; kill -SEGV $$", TimeSpan.FromMinutes(1));
        string source = await Av1SourceAsync();
        api.Enqueue(JobTypes.Transcode, source, TranscodeOutputs);

        WorkerLoop loop = services.GetRequiredService<WorkerLoop>();
        await loop.StartAsync();
        using CancellationTokenSource stopping = new();
        Task<bool> run = loop.RunOnceAsync(stopping.Token);
        while (!File.Exists(started) && !run.IsCompleted)
        {
            await Task.Delay(20);
        }

        await stopping.CancelAsync();
        Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(30)));

        Assert.Null(api.Release);
        JsonElement failure = Assert.IsType<JsonElement>(api.Failure);
        Assert.Contains("SIGSEGV", failure.GetProperty("error").GetString());
    }

    [SvtAv1Fact]
    public async Task UnreadableInput_FailsAsNotRetryable()
    {
        string source = Path.Combine(root, "garbage.mp4");
        await File.WriteAllBytesAsync(source, new byte[4096]);
        api.Enqueue(JobTypes.Transcode, source, TranscodeOutputs);

        Assert.True(await RunOnceAsync());

        Assert.Null(api.Completion);
        JsonElement failure = Assert.IsType<JsonElement>(api.Failure);
        Assert.False(failure.GetProperty("retryable").GetBoolean());
        Assert.Contains("readable", failure.GetProperty("error").GetString());
    }

    [SvtAv1Fact]
    public async Task AnEmptyQueue_ClaimsNothing()
    {
        Assert.False(await RunOnceAsync());
        Assert.Empty(api.Events);
    }

    private async Task<bool> RunOnceAsync()
    {
        WorkerLoop loop = services.GetRequiredService<WorkerLoop>();
        await loop.StartAsync();
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(3));
        return await loop.RunOnceAsync(timeout.Token);
    }

    private void AssertUploadHeaders(IEnumerable<string> names)
    {
        foreach (string name in names)
        {
            FakeWorkerApi.Upload upload = api.Uploads[name];
            Assert.Equal(OutputFiles.ContentType(name), upload.ContentType);
            Assert.Equal("public, max-age=31536000, immutable", upload.CacheControl);
            Assert.True(upload.Length > 0, $"{name} is empty");
        }
    }

    /// <summary>The first report with thumbnails_ready comes after all six frames are uploaded, and before av1.mp4.</summary>
    private void AssertFramesReportedAfterUpload()
    {
        List<string> events = api.Events.ToList();
        int ready = api.Progress.ToList().FindIndex(report => report.GetProperty("thumbnails_ready").GetBoolean());
        Assert.True(ready >= 0, "thumbnails_ready was never reported");
        int readyEvent = events.Select((name, index) => (name, index)).Where(pair => pair.name == "progress")
            .ElementAt(ready).index;
        foreach (string frame in OutputFiles.Frames)
        {
            Assert.True(events.IndexOf($"upload:{frame}") < readyEvent, $"{frame} was uploaded after thumbnails_ready");
        }

        int video = events.IndexOf($"upload:{OutputFiles.Video}");
        Assert.True(video < 0 || video > readyEvent, "av1.mp4 was uploaded before thumbnails_ready");
        Assert.Equal("complete", events[^1]);
    }

    private async Task<string> Av1SourceAsync() =>
        await GenerateAsync("av1.mp4", "testsrc2=size=640x360:rate=30:duration=4", audio: true,
            "-c:v", "libsvtav1", "-preset", "12", "-g", "30", "-pix_fmt", "yuv420p", "-c:a", "libopus",
            "-movflags", "+faststart");

    private async Task<string> GenerateAsync(string name, string video, bool audio, params string[] encode)
    {
        string output = Path.Combine(root, name);
        List<string> args = ["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", video];
        if (audio)
        {
            args.AddRange(["-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000", "-shortest"]);
        }

        args.AddRange(encode);
        args.Add(output);
        await RunAsync("ffmpeg", args);
        return output;
    }

    private async Task<MediaInfo> ProbeAsync(string path) =>
        await services.GetRequiredService<FfmpegRunner>().ProbeAsync(path, CancellationToken.None);

    private static async Task<string> FrameRateAsync(string path) =>
        (await RunAsync("ffprobe", ["-v", "error", "-select_streams", "v:0", "-show_entries", "stream=r_frame_rate",
            "-of", "default=noprint_wrappers=1:nokey=1", path])).Trim();

    private static async Task<string> RunAsync(string tool, IEnumerable<string> args)
    {
        ProcessStartInfo start = new(tool) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using Process process = Process.Start(start)!;
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        string stdout = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, $"{tool} failed: {await stderr}");
        return stdout;
    }

    [GeneratedRegex(@"^av01\.0\.\d\dM\.08,opus$")]
    private static partial Regex Av1WithOpus();
}
