using System.Globalization;

namespace Reelshelf.Worker;

/// <summary>
/// A frame rate as the exact rational ffprobe reports (<c>60000/1001</c> stays that, not 59.94), so the encode's
/// <c>fps</c> filter keeps the source's timing.
/// </summary>
public readonly record struct FrameRate(long Numerator, long Denominator)
{
    /// <summary>The rate used when the source's is unknown or below 1 fps.</summary>
    public static readonly FrameRate Default = new(30, 1);

    /// <summary>The highest rate the output keeps; anything faster (120, 144, 240) is brought down to this.</summary>
    public static readonly FrameRate Max = new(60, 1);

    public double Value => (double)Numerator / Denominator;

    /// <summary>One keyframe a second: the rate rounded, at least 1. Stream-copied trims rely on it.</summary>
    public int KeyframeInterval => Math.Max(1, (int)Math.Round(Value, MidpointRounding.AwayFromZero));

    /// <summary>
    /// The output's rate for a source: its <c>avg_frame_rate</c>, else <c>r_frame_rate</c> when that is <c>0/0</c> or
    /// missing, capped at 60, or 30 when neither is known or the rate is below 1.
    /// </summary>
    public static FrameRate Target(string? averageFrameRate, string? realFrameRate)
    {
        FrameRate? source = Parse(averageFrameRate) ?? Parse(realFrameRate);
        if (source is not { } rate || rate.Value < 1)
        {
            return Default;
        }

        return rate.Value > Max.Value ? Max : rate;
    }

    /// <summary>ffprobe's <c>num/den</c> (or a plain number of frames per second); null when it isn't a positive rate.</summary>
    public static FrameRate? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string[] parts = text.Trim().Split('/');
        if (parts.Length > 2 ||
            !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out long numerator))
        {
            return null;
        }

        long denominator = 1;
        if (parts.Length == 2 &&
            !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out denominator))
        {
            return null;
        }

        return numerator > 0 && denominator > 0 ? new FrameRate(numerator, denominator) : null;
    }

    /// <summary>As ffmpeg's options take it, e.g. <c>60000/1001</c>.</summary>
    public override string ToString() =>
        Denominator == 1
            ? Numerator.ToString(CultureInfo.InvariantCulture)
            : string.Create(CultureInfo.InvariantCulture, $"{Numerator}/{Denominator}");
}
