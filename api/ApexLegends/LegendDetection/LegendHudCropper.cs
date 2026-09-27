using FFMpegCore;
using FFMpegCore.Pipes;

namespace Reelshelf.ApexLegends.LegendDetection;

/// <summary>
/// Cuts the clip owner's squad panel (portrait, name, level bar and upgrade icons) out of a gameplay frame and
/// enlarges it, so the model sees the portrait across many more image patches and without the teammates or the
/// rest of the screen. The region is a fraction of the frame, which matches Apex's default HUD at 16:9.
/// </summary>
public static class LegendHudCropper
{
    public const double PanelWidth = 0.234;
    public const double PanelHeight = 0.17;
    public const int Scale = 3;

    private static readonly string Filter = FormattableString.Invariant(
        $"crop=iw*{PanelWidth}:ih*{PanelHeight}:0:ih*{1 - PanelHeight},scale=iw*{Scale}:ih*{Scale}:flags=lanczos");

    /// <summary>The owner's panel from <paramref name="frame"/> as a JPEG. Throws when ffmpeg cannot read the frame.</summary>
    public static async Task<LegendFrame> CropOwnerPanelAsync(LegendFrame frame, CancellationToken cancellationToken)
    {
        using MemoryStream input = new(frame.Data);
        using MemoryStream output = new();
        await FFMpegArguments
            .FromPipeInput(new StreamPipeSource(input), options => options.ForceFormat("image2pipe"))
            .OutputToPipe(new StreamPipeSink(output), options => options
                .WithCustomArgument($"-vf {Filter}")
                .WithCustomArgument("-frames:v 1 -q:v 2")
                .WithVideoCodec("mjpeg")
                .ForceFormat("image2pipe"))
            .CancellableThrough(cancellationToken)
            .ProcessAsynchronously();

        return new LegendFrame(output.ToArray(), "image/jpeg");
    }
}
