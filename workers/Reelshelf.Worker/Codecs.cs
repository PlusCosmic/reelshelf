using System.Globalization;

namespace Reelshelf.Worker;

/// <summary>
/// The RFC 6381 <c>codecs</c> value for a produced file, as players pass it to <c>mediaCapabilities</c> and
/// <c>canPlayType</c>, e.g. <c>av01.0.08M.08,opus</c>.
/// </summary>
public static class Codecs
{
    /// <summary>
    /// AV1 levels by <c>seq_level_idx</c>: the largest picture (and sides) and display rate each allows, for when
    /// ffprobe doesn't report the level the encoder wrote.
    /// </summary>
    private static readonly (int Index, long MaxPicture, int MaxWidth, int MaxHeight, long MaxDisplayRate)[] Av1Levels =
    [
        (0, 147_456, 2048, 1152, 4_423_680),
        (1, 278_784, 2816, 1584, 8_363_520),
        (4, 665_856, 4352, 2448, 19_975_680),
        (5, 1_065_024, 5504, 3096, 31_950_720),
        (8, 2_359_296, 6144, 3456, 70_778_880),
        (9, 2_359_296, 6144, 3456, 141_557_760),
        (12, 8_912_896, 8192, 4352, 267_386_880),
        (13, 8_912_896, 8192, 4352, 534_773_760)
    ];

    /// <summary>The codecs of <paramref name="media"/>: its video, then its audio when it has any.</summary>
    public static string For(MediaInfo media, double frameRate = 30)
    {
        VideoStreamInfo video = media.Video ?? throw new InvalidOperationException("The output has no video stream");
        string codecs = Video(video, frameRate);
        string? audio = media.Audio is { } stream ? Audio(stream) : null;
        return audio is null ? codecs : $"{codecs},{audio}";
    }

    /// <summary>
    /// <c>av01.P.LLT.DD</c>: profile, level and tier, bit depth (AV1 codec ISO media file format binding, section
    /// "Codecs Parameter String"). The level is the one in the sequence header, as ffprobe reports it; SVT-AV1 writes
    /// the main tier.
    /// </summary>
    public static string Video(VideoStreamInfo video, double frameRate = 30)
    {
        if (video.Codec != "av1")
        {
            throw new InvalidOperationException($"Expected an AV1 stream, found {video.Codec}");
        }

        int profile = video.Profile switch
        {
            "High" => 1,
            "Professional" => 2,
            _ => 0
        };
        int level = video.Level is >= 0 and <= 31 ? video.Level.Value : Av1Level(video.Width, video.Height, frameRate);
        return string.Create(CultureInfo.InvariantCulture, $"av01.{profile}.{level:D2}M.{video.BitDepth:D2}");
    }

    public static string? Audio(AudioStreamInfo audio) => audio.Codec switch
    {
        "opus" => "opus",
        "aac" => audio.Profile == "HE-AAC" ? "mp4a.40.5" : "mp4a.40.2",
        "flac" => "flac",
        "mp3" => "mp3",
        _ => null
    };

    /// <summary>The lowest AV1 level whose picture size and display rate take <paramref name="width"/>×<paramref name="height"/>.</summary>
    public static int Av1Level(int width, int height, double frameRate)
    {
        long picture = (long)width * height;
        foreach ((int index, long maxPicture, int maxWidth, int maxHeight, long maxDisplayRate) in Av1Levels)
        {
            if (picture <= maxPicture && width <= maxWidth && height <= maxHeight && picture * frameRate <= maxDisplayRate)
            {
                return index;
            }
        }

        return 31;
    }
}
