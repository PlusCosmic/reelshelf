namespace Reelshelf.Worker;

/// <summary>
/// When the six still frames are taken, in seconds into the (trimmed) video. They stand in for Bunny's, which legend
/// detection is tuned on: Bunny takes the main thumbnail at the clip's <c>thumbnailTime</c>, which Reelshelf sets to 0
/// (<c>BunnyService</c>), and its five others spread through the video. So <c>thumbnail.jpg</c> is the first frame and
/// <c>thumbnail_1..5.jpg</c> split the clip into six equal parts (1/6 … 5/6), which never lands on the last frame,
/// where a recording often ends on a menu or a fade.
/// </summary>
public static class FrameTimings
{
    public const int ExtraFrames = 5;

    /// <summary>The frame times for <see cref="OutputFiles.Frames"/>, in that order.</summary>
    public static IReadOnlyList<double> For(double lengthSeconds)
    {
        double length = double.IsFinite(lengthSeconds) ? Math.Max(0, lengthSeconds) : 0;
        return [0, .. Enumerable.Range(1, ExtraFrames).Select(index => Math.Round(length * index / (ExtraFrames + 1), 3))];
    }

    /// <summary>The preview loop's start: a third of the way in, or the start when the clip is barely longer than the loop.</summary>
    public static double PreviewStart(double lengthSeconds, double previewSeconds)
    {
        if (lengthSeconds <= previewSeconds)
        {
            return 0;
        }

        return Math.Round(Math.Min(lengthSeconds / 3, lengthSeconds - previewSeconds), 3);
    }
}
