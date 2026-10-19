using System.Globalization;

namespace Reelshelf.Worker;

/// <summary>
/// The ffmpeg command lines for each step (docs/video-pipeline/storage-transcoding-queue.md, "Encoding notes"),
/// without the binary. They are argument lists, never a shell string, so nothing in a path or filter is quoted.
/// </summary>
public static class EncodeArguments
{
    public const int FrameRate = 30;

    /// <summary>The output fits in 1920×1080, or 1080×1920 when the source is portrait, and is never upscaled.</summary>
    public const int MaxLongSide = 1920;

    public const int MaxShortSide = 1080;

    public const double PreviewSeconds = 3;
    public const int PreviewLongSide = 640;
    public const int PreviewShortSide = 360;
    public const int PreviewFrameRate = 15;
    public const int PreviewPreset = 10;
    public const int PreviewCrf = 45;

    /// <summary>
    /// HDR (PQ or HLG) down to SDR BT.709: linearise, convert the primaries, tone-map with Hable's curve and encode
    /// back to BT.709 limited range. zscale reads the source's transfer and primaries from the frames.
    /// </summary>
    public const string ToneMapFilter =
        "zscale=t=linear:npl=100,format=gbrpf32le,zscale=p=bt709,tonemap=tonemap=hable:desat=0,zscale=t=bt709:m=bt709:r=tv";

    /// <summary>The <c>Cache-Control</c> the presigned PUTs sign; outputs are immutable, versioned keys.</summary>
    public const string UploadCacheControl = "public, max-age=31536000, immutable";

    /// <summary>
    /// Fits the picture inside <paramref name="longSide"/>×<paramref name="shortSide"/> (turned for portrait), keeping
    /// its shape, never enlarging it, with even sides as 4:2:0 needs.
    /// </summary>
    public static string ScaleFilter(int longSide = MaxLongSide, int shortSide = MaxShortSide) =>
        string.Create(CultureInfo.InvariantCulture,
            $"scale=w='if(gte(iw,ih),min({longSide},iw),min({shortSide},iw))':h='if(gte(iw,ih),min({shortSide},ih),min({longSide},ih))':force_original_aspect_ratio=decrease:force_divisible_by=2");

    /// <summary>The main encode's filters: constant 30 fps, fitted to 1080p, tone-mapped when HDR, 8-bit 4:2:0.</summary>
    public static string VideoFilter(bool toneMap) =>
        $"fps={FrameRate},{ScaleFilter()}{(toneMap ? "," + ToneMapFilter : "")},format=yuv420p";

    /// <summary>
    /// The source (or the part of it <paramref name="trim"/> keeps) as the AV1 MP4: SVT-AV1 at the target bitrate with
    /// a keyframe every second, Opus audio when there is any, metadata dropped, <c>+faststart</c>, progress on stdout.
    /// Seeking as an input option and re-encoding makes the cut frame-accurate.
    /// </summary>
    public static IReadOnlyList<string> Transcode(
        string input,
        string output,
        WorkerSettings settings,
        TrimRange trim,
        bool toneMap)
    {
        List<string> args = ["-hide_banner", "-nostdin", "-y", "-loglevel", "error"];
        AddTrim(args, trim);
        args.AddRange(["-i", input, "-map", "0:v:0", "-map", "0:a:0?", "-map_metadata", "-1", "-map_chapters", "-1"]);
        args.AddRange(["-vf", VideoFilter(toneMap), "-fps_mode", "cfr"]);
        args.AddRange(["-c:v", "libsvtav1", "-preset", Invariant(settings.Preset), "-b:v", $"{Invariant(settings.TargetBitrateKbps)}k"]);
        args.AddRange(["-g", Invariant(FrameRate)]);
        if (settings.LogicalProcessors > 0)
        {
            args.AddRange(["-svtav1-params", $"lp={Invariant(settings.LogicalProcessors)}"]);
        }

        if (toneMap)
        {
            args.AddRange(["-color_primaries", "bt709", "-color_trc", "bt709", "-colorspace", "bt709"]);
        }

        args.AddRange(["-c:a", "libopus", "-b:a", $"{Invariant(settings.AudioBitrateKbps)}k", "-ac", "2"]);
        args.AddRange(["-movflags", "+faststart", "-progress", "pipe:1", "-nostats", output]);
        return args;
    }

    /// <summary>
    /// A cut of an existing output without re-encoding. The cut starts on the keyframe at or before the trim's start,
    /// which <c>-g 30</c> keeps within a second.
    /// </summary>
    public static IReadOnlyList<string> TrimCopy(string input, string output, TrimRange trim)
    {
        List<string> args = ["-hide_banner", "-nostdin", "-y", "-loglevel", "error"];
        AddTrim(args, trim);
        args.AddRange(["-i", input, "-map", "0:v:0", "-map", "0:a:0?", "-map_metadata", "-1", "-map_chapters", "-1"]);
        args.AddRange(["-c", "copy", "-avoid_negative_ts", "make_zero"]);
        args.AddRange(["-movflags", "+faststart", "-progress", "pipe:1", "-nostats", output]);
        return args;
    }

    /// <summary>
    /// Ends the frame filter: full-range 8-bit 4:2:0, the only kind of JPEG the mjpeg encoder writes without
    /// <c>-strict unofficial</c>. Without it, ffmpeg 7.1 can hand the encoder limited-range YUV for some sources and
    /// it refuses to open ("Non full-range YUV is non-standard"), so no frames are made.
    /// </summary>
    public const string JpegFilter = "scale=out_range=full,format=yuvj420p";

    /// <summary>One JPEG frame at <paramref name="seconds"/> into the input, at the output's size (tone-mapped when HDR).</summary>
    public static IReadOnlyList<string> Frame(string input, string output, double seconds, bool toneMap)
    {
        string filter = (toneMap ? $"{ScaleFilter()},{ToneMapFilter}" : ScaleFilter()) + "," + JpegFilter;
        return
        [
            "-hide_banner", "-nostdin", "-y", "-loglevel", "error",
            "-ss", TrimRange.Seconds(seconds), "-i", input,
            "-map", "0:v:0", "-an", "-frames:v", "1", "-vf", filter, "-color_range", "pc",
            "-q:v", "2", "-update", "1", output
        ];
    }

    /// <summary>
    /// The animated preview: a few silent seconds from <paramref name="startSeconds"/>, small and at 15 fps, as a
    /// constant-quality AV1 MP4 that a card can loop.
    /// </summary>
    public static IReadOnlyList<string> Preview(string input, string output, double startSeconds, double lengthSeconds, bool toneMap)
    {
        string filter = $"fps={PreviewFrameRate},{ScaleFilter(PreviewLongSide, PreviewShortSide)}" +
                        $"{(toneMap ? "," + ToneMapFilter : "")},format=yuv420p";
        return
        [
            "-hide_banner", "-nostdin", "-y", "-loglevel", "error",
            "-ss", TrimRange.Seconds(startSeconds), "-t", TrimRange.Seconds(Math.Min(PreviewSeconds, lengthSeconds)),
            "-i", input,
            "-map", "0:v:0", "-an", "-map_metadata", "-1", "-map_chapters", "-1",
            "-vf", filter, "-fps_mode", "cfr",
            "-c:v", "libsvtav1", "-preset", Invariant(PreviewPreset), "-crf", Invariant(PreviewCrf),
            "-g", Invariant(PreviewFrameRate * 2),
            "-movflags", "+faststart", output
        ];
    }

    /// <summary>How long ffmpeg waits on a stalled read of the playlist or a segment before giving up on it.</summary>
    public static readonly TimeSpan HlsReadTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// A <c>migrate</c> job's HLS input copied into a local MP4 without re-encoding. ffmpeg's default stream selection
    /// takes the highest-resolution variant (and only downloads that one), and the MP4 muxer converts ADTS AAC itself.
    /// Logged at warning level, so a segment ffmpeg skipped shows in its output (<see cref="HlsRemux"/>). Only network
    /// protocols are allowed, so a playlist can't point ffmpeg at local files.
    /// </summary>
    public static IReadOnlyList<string> HlsRemux(string playlistUrl, string output) =>
    [
        "-hide_banner", "-nostdin", "-y", "-loglevel", "warning",
        "-protocol_whitelist", "https,http,tls,tcp,crypto",
        "-rw_timeout", ((long)HlsReadTimeout.TotalMicroseconds).ToString(CultureInfo.InvariantCulture),
        "-seg_max_retry", "3",
        "-f", "hls", "-i", playlistUrl,
        "-c", "copy", "-sn", "-dn", "-f", "mp4", output
    ];

    /// <summary>ffprobe's description of a file as JSON, which <see cref="MediaInfo.Parse"/> reads.</summary>
    public static IReadOnlyList<string> Probe(string input) =>
        ["-v", "error", "-print_format", "json", "-show_format", "-show_streams", input];

    private static void AddTrim(List<string> args, TrimRange trim)
    {
        if (!trim.IsCut)
        {
            return;
        }

        if (trim.StartSeconds > 0)
        {
            args.AddRange(["-ss", TrimRange.Seconds(trim.StartSeconds)]);
        }

        args.AddRange(["-t", TrimRange.Seconds(trim.LengthSeconds)]);
    }

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);
}
