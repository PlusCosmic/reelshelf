using FFMpegCore;
using FFMpegCore.Pipes;
using Reelshelf.ApexLegends.LegendDetection;
using Xunit;

namespace Reelshelf.Test.ApexLegends;

/// <summary>Runs the real ffmpeg, which CI installs, against a generated 1080p frame.</summary>
public class LegendHudCropperTests
{
    [Fact]
    public async Task CropOwnerPanel_CutsTheBottomLeftPanel_AndEnlargesIt()
    {
        LegendFrame frame = await GenerateFrameAsync(1920, 1080);

        LegendFrame panel = await LegendHudCropper.CropOwnerPanelAsync(frame, CancellationToken.None);

        Assert.Equal("image/jpeg", panel.MediaType);
        Assert.Equal([0xFF, 0xD8], panel.Data[..2]);
        IMediaAnalysis analysis = await FFProbe.AnalyseAsync(new MemoryStream(panel.Data));
        // 23.4% by 17% of 1080p is about 449×184, rounded to even for JPEG, then 3×.
        Assert.InRange(analysis.PrimaryVideoStream!.Width, 1340, 1350);
        Assert.InRange(analysis.PrimaryVideoStream.Height, 548, 556);
    }

    [Fact]
    public async Task Overview_HalvesA1080pFrame_AndLeavesSmallerFramesAlone()
    {
        LegendFrame overview = await LegendHudCropper.OverviewAsync(await GenerateFrameAsync(1920, 1080), CancellationToken.None);
        IMediaAnalysis analysis = await FFProbe.AnalyseAsync(new MemoryStream(overview.Data));
        Assert.Equal(960, analysis.PrimaryVideoStream!.Width);
        Assert.Equal(540, analysis.PrimaryVideoStream.Height);

        LegendFrame small = await LegendHudCropper.OverviewAsync(await GenerateFrameAsync(640, 360), CancellationToken.None);
        Assert.Equal(640, (await FFProbe.AnalyseAsync(new MemoryStream(small.Data))).PrimaryVideoStream!.Width);
    }

    [Fact]
    public async Task CropOwnerPanel_Throws_WhenTheFrameIsNotAnImage()
    {
        await Assert.ThrowsAnyAsync<Exception>(() =>
            LegendHudCropper.CropOwnerPanelAsync(new LegendFrame([1, 2, 3], "image/jpeg"), CancellationToken.None));
    }

    private static async Task<LegendFrame> GenerateFrameAsync(int width, int height)
    {
        using MemoryStream output = new();
        await FFMpegArguments
            .FromFileInput($"testsrc2=size={width}x{height}", verifyExists: false, options => options.ForceFormat("lavfi"))
            .OutputToPipe(new StreamPipeSink(output), options => options
                .WithCustomArgument("-frames:v 1")
                .WithVideoCodec("mjpeg")
                .ForceFormat("image2pipe"))
            .ProcessAsynchronously();
        return new LegendFrame(output.ToArray(), "image/jpeg");
    }
}
