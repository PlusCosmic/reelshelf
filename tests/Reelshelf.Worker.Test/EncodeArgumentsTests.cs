using Xunit;

namespace Reelshelf.Worker.Test;

public class EncodeArgumentsTests
{
    private static readonly WorkerSettings Settings = new() { Preset = 7, LogicalProcessors = 4, TargetBitrateKbps = 4000 };

    private static readonly TrimRange Whole = new(0, 60, false);

    [Fact]
    public void Transcode_EncodesSvtAv1_At30FpsWithAKeyframeEverySecond_AndOpus()
    {
        IReadOnlyList<string> args = EncodeArguments.Transcode("in", "out.mp4", Settings, Whole, toneMap: false);

        AssertOption(args, "-c:v", "libsvtav1");
        AssertOption(args, "-preset", "7");
        AssertOption(args, "-b:v", "4000k");
        AssertOption(args, "-g", "30");
        AssertOption(args, "-svtav1-params", "lp=4");
        AssertOption(args, "-fps_mode", "cfr");
        AssertOption(args, "-movflags", "+faststart");
        AssertOption(args, "-c:a", "libopus");
        AssertOption(args, "-b:a", "128k");
        AssertOption(args, "-map_metadata", "-1");
        AssertOption(args, "-progress", "pipe:1");
        Assert.Equal("out.mp4", args[^1]);

        string filter = Option(args, "-vf");
        Assert.StartsWith("fps=30,scale=", filter);
        Assert.EndsWith(",format=yuv420p", filter);
        Assert.DoesNotContain("zscale", filter);
        Assert.DoesNotContain("-ss", args);
        Assert.DoesNotContain("-t", args);
        Assert.DoesNotContain("-color_trc", args);
    }

    [Fact]
    public void Transcode_LeavesLpToSvtAv1_WhenUnset()
    {
        IReadOnlyList<string> args = EncodeArguments.Transcode("in", "out.mp4", new WorkerSettings(), Whole, toneMap: false);

        Assert.DoesNotContain("-svtav1-params", args);
        AssertOption(args, "-preset", "8");
    }

    [Fact]
    public void Transcode_CutsTheTrim_AsInputOptions_SoTheReencodeIsFrameAccurate()
    {
        IReadOnlyList<string> args = EncodeArguments.Transcode("in", "out.mp4", Settings, new TrimRange(12.5, 30.25, true),
            toneMap: false);

        int input = args.ToList().IndexOf("-i");
        AssertOption(args, "-ss", "12.5");
        AssertOption(args, "-t", "30.25");
        Assert.True(args.ToList().IndexOf("-ss") < input);
        Assert.True(args.ToList().IndexOf("-t") < input);
    }

    [Fact]
    public void Transcode_ToneMapsHdr_AndTagsTheOutputBt709()
    {
        IReadOnlyList<string> args = EncodeArguments.Transcode("in", "out.mp4", Settings, Whole, toneMap: true);

        Assert.Contains(EncodeArguments.ToneMapFilter, Option(args, "-vf"));
        AssertOption(args, "-color_primaries", "bt709");
        AssertOption(args, "-color_trc", "bt709");
        AssertOption(args, "-colorspace", "bt709");
    }

    [Fact]
    public void ScaleFilter_FitsInside1080p_TurnedForPortrait_WithoutUpscaling()
    {
        string filter = EncodeArguments.ScaleFilter();

        Assert.Equal(
            "scale=w='if(gte(iw,ih),min(1920,iw),min(1080,iw))':h='if(gte(iw,ih),min(1080,ih),min(1920,ih))'" +
            ":force_original_aspect_ratio=decrease:force_divisible_by=2",
            filter);
    }

    [Fact]
    public void TrimCopy_StreamCopiesTheRange()
    {
        IReadOnlyList<string> args = EncodeArguments.TrimCopy("in.mp4", "out.mp4", new TrimRange(5, 10, true));

        AssertOption(args, "-c", "copy");
        AssertOption(args, "-ss", "5");
        AssertOption(args, "-t", "10");
        AssertOption(args, "-movflags", "+faststart");
        Assert.DoesNotContain("-vf", args);
        Assert.DoesNotContain("libsvtav1", args);
    }

    [Fact]
    public void Frame_TakesOneJpegAtTheTime()
    {
        IReadOnlyList<string> args = EncodeArguments.Frame("in", "thumbnail_3.jpg", 30.1234, toneMap: false);

        AssertOption(args, "-ss", "30.123");
        AssertOption(args, "-frames:v", "1");
        Assert.Equal($"{EncodeArguments.ScaleFilter()},{EncodeArguments.JpegFilter}", Option(args, "-vf"));
        AssertOption(args, "-color_range", "pc");
        Assert.Equal("thumbnail_3.jpg", args[^1]);
    }

    [Fact]
    public void Frame_ToneMapsBeforeConvertingToFullRangeJpeg()
    {
        IReadOnlyList<string> args = EncodeArguments.Frame("in", "thumbnail.jpg", 0, toneMap: true);

        Assert.EndsWith($"{EncodeArguments.ToneMapFilter},{EncodeArguments.JpegFilter}", Option(args, "-vf"));
    }

    [Fact]
    public void Preview_IsShortSilentAndSmall()
    {
        IReadOnlyList<string> args = EncodeArguments.Preview("in", "preview.mp4", 20, 60, toneMap: false);

        AssertOption(args, "-ss", "20");
        AssertOption(args, "-t", "3");
        Assert.Contains("-an", args);
        AssertOption(args, "-c:v", "libsvtav1");
        Assert.Contains("min(640,iw)", Option(args, "-vf"));
        Assert.StartsWith("fps=15,", Option(args, "-vf"));

        Assert.Equal("1.5", Option(EncodeArguments.Preview("in", "preview.mp4", 0, 1.5, toneMap: false), "-t"));
    }

    [Fact]
    public void Command_RunsUnderNice_WhenNicenessIsSet()
    {
        (string fileName, IReadOnlyList<string> args) = FfmpegRunner.Command("ffmpeg", ["-i", "x"], 10);
        Assert.Equal("nice", fileName);
        Assert.Equal(["-n", "10", "ffmpeg", "-i", "x"], args);

        (fileName, args) = FfmpegRunner.Command("ffmpeg", ["-i", "x"], 0);
        Assert.Equal("ffmpeg", fileName);
        Assert.Equal(["-i", "x"], args);
    }

    private static string Option(IReadOnlyList<string> args, string name)
    {
        int index = args.ToList().IndexOf(name);
        Assert.True(index >= 0 && index + 1 < args.Count, $"{name} is missing");
        return args[index + 1];
    }

    private static void AssertOption(IReadOnlyList<string> args, string name, string value)
    {
        Assert.Equal(value, Option(args, name));
    }
}
