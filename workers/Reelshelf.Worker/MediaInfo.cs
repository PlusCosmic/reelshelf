using System.Globalization;
using System.Text.Json;

namespace Reelshelf.Worker;

/// <summary>What ffprobe says about a file: its first video and audio streams and its duration.</summary>
public sealed record MediaInfo(double DurationSeconds, long? SizeBytes, VideoStreamInfo? Video, AudioStreamInfo? Audio)
{
    /// <summary>
    /// Parses <c>ffprobe -show_streams -show_format -of json</c>. The duration is the container's, else the longest
    /// stream's; 0 when neither is known.
    /// </summary>
    public static MediaInfo Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        VideoStreamInfo? video = null;
        AudioStreamInfo? audio = null;
        double streamDuration = 0;
        if (root.TryGetProperty("streams", out JsonElement streams) && streams.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement stream in streams.EnumerateArray())
            {
                string? type = Text(stream, "codec_type");
                streamDuration = Math.Max(streamDuration, Number(stream, "duration") ?? 0);
                if (type == "video" && video is null && !IsAttachedPicture(stream))
                {
                    video = new VideoStreamInfo(
                        Text(stream, "codec_name") ?? "",
                        Text(stream, "profile"),
                        (int?)Number(stream, "level"),
                        (int)(Number(stream, "width") ?? 0),
                        (int)(Number(stream, "height") ?? 0),
                        Text(stream, "pix_fmt"),
                        Text(stream, "color_transfer"),
                        Text(stream, "color_primaries"),
                        FrameRate.Target(Text(stream, "avg_frame_rate"), Text(stream, "r_frame_rate")));
                }
                else if (type == "audio" && audio is null)
                {
                    audio = new AudioStreamInfo(Text(stream, "codec_name") ?? "", Text(stream, "profile"));
                }
            }
        }

        double? formatDuration = null;
        long? size = null;
        if (root.TryGetProperty("format", out JsonElement format) && format.ValueKind == JsonValueKind.Object)
        {
            formatDuration = Number(format, "duration");
            size = (long?)Number(format, "size");
        }

        double duration = formatDuration is > 0 ? formatDuration.Value : streamDuration;
        return new MediaInfo(double.IsFinite(duration) ? duration : 0, size, video, audio);
    }

    /// <summary>A cover image in an audio file shows up as a video stream; it isn't one.</summary>
    private static bool IsAttachedPicture(JsonElement stream) =>
        stream.TryGetProperty("disposition", out JsonElement disposition) &&
        disposition.ValueKind == JsonValueKind.Object &&
        disposition.TryGetProperty("attached_pic", out JsonElement attached) &&
        attached.ValueKind == JsonValueKind.Number && attached.GetInt32() == 1;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>ffprobe writes most numbers as strings ("12.5") and some as numbers.</summary>
    private static double? Number(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number)
        {
            return value.GetDouble();
        }

        return value.ValueKind == JsonValueKind.String &&
               double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : null;
    }
}

public sealed record VideoStreamInfo(
    string Codec,
    string? Profile,
    int? Level,
    int Width,
    int Height,
    string? PixelFormat,
    string? ColorTransfer,
    string? ColorPrimaries,
    FrameRate TargetFrameRate)
{
    /// <summary>PQ (HDR10, Dolby Vision's base layer) or HLG, which need tone-mapping down to SDR.</summary>
    public bool IsHdr => ColorTransfer is "smpte2084" or "arib-std-b67";

    /// <summary>The bit depth from the pixel format (yuv420p10le is 10); 8 when it doesn't say.</summary>
    public int BitDepth
    {
        get
        {
            string format = PixelFormat ?? "";
            if (format.Contains("12", StringComparison.Ordinal))
            {
                return 12;
            }

            return format.Contains("10", StringComparison.Ordinal) ? 10 : 8;
        }
    }
}

public sealed record AudioStreamInfo(string Codec, string? Profile);
