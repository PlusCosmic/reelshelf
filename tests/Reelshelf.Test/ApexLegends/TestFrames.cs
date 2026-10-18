using FFMpegCore;
using Reelshelf.ApexLegends.LegendDetection;

namespace Reelshelf.Test.ApexLegends;

/// <summary>Generated test frames, made with the real ffmpeg, which CI installs.</summary>
internal static class TestFrames
{
    /// <summary>
    /// One <c>testsrc2</c> frame of the given size as a JPEG. It is written to a temp file rather than to
    /// FFMpegCore's output pipe, which now and then hands back the image cut short (see <see cref="LegendHudCropper"/>).
    /// </summary>
    public static async Task<LegendFrame> JpegAsync(int width, int height)
    {
        string output = Path.Combine(Path.GetTempPath(), $"legend-test-{Guid.NewGuid():N}.jpg");
        try
        {
            await FFMpegArguments
                .FromFileInput($"testsrc2=size={width}x{height}", verifyExists: false, options => options.ForceFormat("lavfi"))
                .OutputToFile(output, overwrite: true, options => options
                    .WithCustomArgument("-frames:v 1")
                    .WithVideoCodec("mjpeg")
                    .ForceFormat("image2pipe"))
                .ProcessAsynchronously();
            return new LegendFrame(await File.ReadAllBytesAsync(output), "image/jpeg");
        }
        finally
        {
            File.Delete(output);
        }
    }
}
