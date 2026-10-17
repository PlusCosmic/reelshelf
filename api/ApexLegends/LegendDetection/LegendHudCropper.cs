using FFMpegCore;
using FFMpegCore.Pipes;

namespace Reelshelf.ApexLegends.LegendDetection;

/// <summary>
/// Prepares each gameplay frame as two images. The close-up cuts the bottom-left squad panel (portrait, name,
/// level bar and upgrade icons) out of the frame and enlarges it, so the model sees the portrait across many more
/// image patches; the region is a fraction of the frame, which matches Apex's default HUD at 16:9. The overview is
/// the whole frame at half resolution, so the model can tell what the close-up shows: the clip owner playing, a
/// spectated teammate, or a menu.
/// </summary>
public static class LegendHudCropper
{
    public const double PanelWidth = 0.234;
    public const double PanelHeight = 0.17;
    public const int Scale = 3;

    /// <summary>Overviews are at most this wide: half of 1080p, where HUD text is still legible.</summary>
    public const int OverviewWidth = 960;

    private static readonly string PanelFilter = FormattableString.Invariant(
        $"crop=iw*{PanelWidth}:ih*{PanelHeight}:0:ih*{1 - PanelHeight},scale=iw*{Scale}:ih*{Scale}:flags=lanczos");

    private static readonly string OverviewFilter = $"scale='min({OverviewWidth},iw)':-2:flags=lanczos";

    private static readonly string[] SheetHalfFilters = ["crop=iw/2:ih:0:0", "crop=iw/2:ih:iw/2:0"];

    /// <summary>The bottom-left panel from <paramref name="frame"/> as a JPEG. Throws when ffmpeg cannot read the frame.</summary>
    public static Task<LegendFrame> CropOwnerPanelAsync(LegendFrame frame, CancellationToken cancellationToken)
    {
        return FilterAsync(frame, PanelFilter, cancellationToken);
    }

    /// <summary>The whole of <paramref name="frame"/> scaled down to <see cref="OverviewWidth"/>, as a JPEG.</summary>
    public static Task<LegendFrame> OverviewAsync(LegendFrame frame, CancellationToken cancellationToken)
    {
        return FilterAsync(frame, OverviewFilter, cancellationToken);
    }

    /// <summary>
    /// <paramref name="frame"/> scaled, keeping its shape, so neither side is over <paramref name="maxSide"/>
    /// pixels and, when it is small, so both sides reach <paramref name="minSide"/> and it has at least
    /// <paramref name="minPixels"/> pixels, as a JPEG. An image already within bounds keeps its size. Sides are
    /// rounded up to even numbers, which JPEG encoding needs.
    /// </summary>
    public static Task<LegendFrame> FitAsync(
        LegendFrame frame,
        int minSide,
        int maxSide,
        int minPixels,
        CancellationToken cancellationToken)
    {
        string factor = FormattableString.Invariant(
            $"if(gt(max(iw,ih),{maxSide}),{maxSide}/max(iw,ih),max(1,max({minSide}/min(iw,ih),sqrt({minPixels}/(iw*ih)))))");
        string filter = $"scale=w='ceil(iw*{factor}/2)*2':h='ceil(ih*{factor}/2)*2':flags=lanczos";
        return FilterAsync(frame, filter, cancellationToken);
    }

    /// <summary>Both images for one frame, as the recognizer sends them.</summary>
    public static async Task<LegendScreenshot> PrepareAsync(LegendFrame frame, CancellationToken cancellationToken)
    {
        Task<LegendFrame> overview = OverviewAsync(frame, cancellationToken);
        Task<LegendFrame> panel = CropOwnerPanelAsync(frame, cancellationToken);
        return new LegendScreenshot(await overview, await panel);
    }

    /// <summary>
    /// The reference sheet's left and right halves, seven legends a row each, as PNGs. The whole sheet is too wide for
    /// Jev, which takes at most 1,568 pixels a side, and Claude would scale it down until the labels blur.
    /// </summary>
    public static async Task<IReadOnlyList<LegendFrame>> SplitReferenceSheetAsync(
        byte[] sheet,
        CancellationToken cancellationToken)
    {
        LegendFrame frame = new(sheet, "image/png");
        return await Task.WhenAll(SheetHalfFilters.Select(filter =>
            FilterAsync(frame, filter, cancellationToken, png: true)));
    }

    private static async Task<LegendFrame> FilterAsync(
        LegendFrame frame,
        string filter,
        CancellationToken cancellationToken,
        bool png = false)
    {
        using MemoryStream input = new(frame.Data);
        using MemoryStream output = new();
        await FFMpegArguments
            .FromPipeInput(new StreamPipeSource(input), options => options.ForceFormat("image2pipe"))
            .OutputToPipe(new StreamPipeSink(output), options => options
                .WithCustomArgument($"-vf {filter}")
                .WithCustomArgument(png ? "-frames:v 1" : "-frames:v 1 -q:v 2")
                .WithVideoCodec(png ? "png" : "mjpeg")
                .ForceFormat("image2pipe"))
            .CancellableThrough(cancellationToken)
            .ProcessAsynchronously();

        return new LegendFrame(output.ToArray(), png ? "image/png" : "image/jpeg");
    }
}
