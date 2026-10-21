using Xunit;

namespace Reelshelf.Worker.Test;

public class EncodeArgumentsTests
{
    private static readonly WorkerSettings Settings = new()
    {
        Preset = 7, LogicalProcessors = 4, TargetBitrateKbps = 4000, HighFrameRateBitrateKbps = 6000
    };

    private static readonly FrameRate Thirty = new(30, 1);

    private static readonly TrimRange Whole = new(0, 60, false);

    [Fact]
    public void Transcode_EncodesSvtAv1_At30FpsWithAKeyframeEverySecond_AndOpus()
    {
        IReadOnlyList<string> args = EncodeArguments.Transcode("in", "out.mp4", Settings, Whole, toneMap: false, Thirty);

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
        IReadOnlyList<string> args = EncodeArguments.Transcode("in", "out.mp4", new WorkerSettings(), Whole, toneMap: false, Thirty);

        Assert.DoesNotContain("-svtav1-params", args);
        AssertOption(args, "-preset", "8");
    }

    [Fact]
    public void Transcode_CutsTheTrim_AsInputOptions_SoTheReencodeIsFrameAccurate()
    {
        IReadOnlyList<string> args = EncodeArguments.Transcode("in", "out.mp4", Settings, new TrimRange(12.5, 30.25, true),
            toneMap: false, Thirty);

        int input = args.ToList().IndexOf("-i");
        AssertOption(args, "-ss", "12.5");
        AssertOption(args, "-t", "30.25");
        Assert.True(args.ToList().IndexOf("-ss") < input);
        Assert.True(args.ToList().IndexOf("-t") < input);
    }

    [Fact]
    public void Transcode_ToneMapsHdr_AndTagsTheOutputBt709()
    {
        IReadOnlyList<string> args = EncodeArguments.Transcode("in", "out.mp4", Settings, Whole, toneMap: true, Thirty);

        Assert.Contains(EncodeArguments.ToneMapFilter, Option(args, "-vf"));
        AssertOption(args, "-color_primaries", "bt709");
        AssertOption(args, "-color_trc", "bt709");
        AssertOption(args, "-colorspace", "bt709");
    }

    [Fact]
    public void Transcode_KeepsA60FpsSource_WithAKeyframeEverySecond_AndTheHighFrameRateBitrate()
    {
        IReadOnlyList<string> args = EncodeArguments.Transcode("in", "out.mp4", Settings, Whole, toneMap: false,
            new FrameRate(60, 1));

        Assert.StartsWith("fps=60,scale=", Option(args, "-vf"));
        AssertOption(args, "-fps_mode", "cfr");
        AssertOption(args, "-g", "60");
        AssertOption(args, "-b:v", "6000k");
    }

    [Fact]
    public void Transcode_KeepsAnNtscRateExact()
    {
        IReadOnlyList<string> args = EncodeArguments.Transcode("in", "out.mp4", Settings, Whole, toneMap: false,
            new FrameRate(60000, 1001));

        Assert.StartsWith("fps=60000/1001,", Option(args, "-vf"));
        AssertOption(args, "-g", "60");
        AssertOption(args, "-b:v", "6000k");

        args = EncodeArguments.Transcode("in", "out.mp4", Settings, Whole, toneMap: false, new FrameRate(30000, 1001));
        Assert.StartsWith("fps=30000/1001,", Option(args, "-vf"));
        AssertOption(args, "-g", "30");
        AssertOption(args, "-b:v", "4000k");
    }

    [Theory]
    [InlineData("60/1", "60/1", "60")]
    [InlineData("60000/1001", "60000/1001", "60000/1001")]
    [InlineData("30/1", "30/1", "30")]
    [InlineData("30000/1001", "30000/1001", "30000/1001")]
    [InlineData("144/1", "144/1", "60")]
    [InlineData("240/1", "0/0", "60")]
    [InlineData("0/0", "0/0", "30")]
    [InlineData(null, null, "30")]
    [InlineData("1/2", "1/2", "30")]
    [InlineData("0/0", "60/1", "60")]
    [InlineData(null, "25/1", "25")]
    [InlineData("2987/100", "30/1", "2987/100")]
    [InlineData("nonsense", "24/1", "24")]
    public void TargetFrameRate_KeepsTheSourceRate_CappedAt60_Else30(string? average, string? real, string expected)
    {
        Assert.Equal(expected, FrameRate.Target(average, real).ToString());
    }

    [Theory]
    [InlineData(60, 1, 60)]
    [InlineData(60000, 1001, 60)]
    [InlineData(30, 1, 30)]
    [InlineData(2987, 100, 30)]
    [InlineData(25, 1, 25)]
    public void KeyframeInterval_IsOneSecondAtTheRate(long numerator, long denominator, int expected)
    {
        Assert.Equal(expected, new FrameRate(numerator, denominator).KeyframeInterval);
    }

    [Fact]
    public void MediaInfo_ReadsTheTargetFrameRate_FromAvgFrameRate_ElseRFrameRate()
    {
        static MediaInfo Parse(string rates) => MediaInfo.Parse(
            """{"streams": [{"codec_type": "video", "codec_name": "h264", "width": 1920, "height": 1080, """ + rates +
            """}], "format": {"duration": "10"}}""");

        Assert.Equal(new FrameRate(60000, 1001),
            Parse("\"avg_frame_rate\": \"60000/1001\", \"r_frame_rate\": \"60000/1001\"").Video!.TargetFrameRate);
        Assert.Equal(new FrameRate(60, 1),
            Parse("\"avg_frame_rate\": \"0/0\", \"r_frame_rate\": \"144/1\"").Video!.TargetFrameRate);
        Assert.Equal(FrameRate.Default, Parse("\"codec_tag\": \"0\"").Video!.TargetFrameRate);
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
