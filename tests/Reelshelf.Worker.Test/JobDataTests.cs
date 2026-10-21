using System.Text.Json;
using Xunit;

namespace Reelshelf.Worker.Test;

/// <summary>Frame timing, trim input, codecs strings, ffprobe parsing, settings and backoff.</summary>
public class JobDataTests
{
    [Fact]
    public void FrameTimings_StartWithTheFirstFrame_ThenSplitTheClipIntoSixths()
    {
        Assert.Equal([0, 10, 20, 30, 40, 50], FrameTimings.For(60));
        Assert.Equal([0, 0.5, 1, 1.5, 2, 2.5], FrameTimings.For(3));
        Assert.Equal([0, 0, 0, 0, 0, 0], FrameTimings.For(0));
        Assert.Equal(OutputFiles.Frames.Count, FrameTimings.For(12.3).Count);
    }

    [Fact]
    public void FrameTimings_PreviewStartsAThirdIn_ButStaysInsideTheClip()
    {
        Assert.Equal(20, FrameTimings.PreviewStart(60, 3));
        Assert.Equal(1, FrameTimings.PreviewStart(4, 3));
        Assert.Equal(0, FrameTimings.PreviewStart(2, 3));
    }

    [Fact]
    public void OutputFiles_AreTheContractsKeys()
    {
        Assert.Equal(
            ["thumbnail.jpg", "thumbnail_1.jpg", "thumbnail_2.jpg", "thumbnail_3.jpg", "thumbnail_4.jpg", "thumbnail_5.jpg"],
            OutputFiles.Frames);
        Assert.Equal("image/jpeg", OutputFiles.ContentType("thumbnail_2.jpg"));
        Assert.Equal("video/mp4", OutputFiles.ContentType("av1.mp4"));
        Assert.Equal("video/mp4", OutputFiles.ContentType("preview.mp4"));
    }

    [Fact]
    public void JobInput_ReadsTrimAndFrameAccuracy_AndIgnoresTheRest()
    {
        JobInput input = JobInput.Parse(Json("""
            {"source_key": "uploads/x", "trim_start_seconds": 2.5, "trim_end_seconds": 9, "frame_accurate": true, "x": 1}
            """));

        Assert.Equal(new JobInput(2.5, 9, true), input);
        Assert.Equal(new TrimRange(2.5, 6.5, true), input.Resolve(30));

        JobInput none = JobInput.Parse(Json("""{"source_key": "uploads/x"}"""));
        Assert.False(none.HasTrim);
        Assert.Equal(new TrimRange(0, 30, false), none.Resolve(30));
        Assert.False(JobInput.Parse(Json("null")).HasTrim);
    }

    [Fact]
    public void JobInput_ClampsATrimToTheSource_AndRefusesOneThatKeepsNothing()
    {
        Assert.Equal(new TrimRange(10, 20, true), new JobInput(10, 99, false).Resolve(30));
        Assert.Equal(new TrimRange(0, 30, false), new JobInput(-1, 30, false).Resolve(30));

        JobFailedException failure = Assert.Throws<JobFailedException>(() => new JobInput(40, null, false).Resolve(30));
        Assert.False(failure.Retryable);
        Assert.Throws<JobFailedException>(() => new JobInput(10, 5, false).Resolve(30));
    }

    [Theory]
    [InlineData("Main", 8, "yuv420p", 1920, 1080, "av01.0.08M.08")]
    [InlineData("Main", 5, "yuv420p", 1280, 720, "av01.0.05M.08")]
    [InlineData("Main", 8, "yuv420p10le", 1920, 1080, "av01.0.08M.10")]
    [InlineData("High", 12, "yuv444p", 3840, 2160, "av01.1.12M.08")]
    [InlineData("Main", null, "yuv420p", 1920, 1080, "av01.0.08M.08")]
    [InlineData("Main", -99, "yuv420p", 854, 480, "av01.0.04M.08")]
    public void Codecs_DescribeTheAv1Stream(string profile, int? level, string pixelFormat, int width, int height, string expected)
    {
        VideoStreamInfo video = new("av1", profile, level, width, height, pixelFormat, "bt709", "bt709", FrameRate.Default);

        Assert.Equal(expected, Codecs.Video(video));
    }

    [Fact]
    public void Codecs_AppendTheAudio()
    {
        VideoStreamInfo video = new("av1", "Main", 8, 1920, 1080, "yuv420p", null, null, FrameRate.Default);

        Assert.Equal("av01.0.08M.08,opus", Codecs.For(new MediaInfo(10, 1, video, new AudioStreamInfo("opus", null))));
        Assert.Equal("av01.0.08M.08", Codecs.For(new MediaInfo(10, 1, video, null)));
        Assert.Throws<InvalidOperationException>(() => Codecs.Video(video with { Codec = "h264" }));
    }

    [Theory]
    [InlineData(426, 240, 0)]
    [InlineData(640, 360, 1)]
    [InlineData(1280, 720, 5)]
    [InlineData(1080, 1920, 8)]
    [InlineData(3840, 2160, 12)]
    public void Av1Level_IsTheLowestThatFits(int width, int height, int level)
    {
        Assert.Equal(level, Codecs.Av1Level(width, height, 30));
    }

    [Fact]
    public void MediaInfo_ReadsFfprobeJson()
    {
        MediaInfo media = MediaInfo.Parse("""
            {
              "streams": [
                {"codec_type": "video", "codec_name": "mjpeg", "width": 300, "height": 300, "disposition": {"attached_pic": 1}},
                {"codec_type": "video", "codec_name": "hevc", "profile": "Main 10", "level": 153, "width": 3840,
                 "height": 2160, "pix_fmt": "yuv420p10le", "color_transfer": "smpte2084", "color_primaries": "bt2020",
                 "duration": "12.480000"},
                {"codec_type": "audio", "codec_name": "aac", "profile": "LC", "duration": "12.500000"}
              ],
              "format": {"duration": "12.500000", "size": "48213004"}
            }
            """);

        Assert.Equal(12.5, media.DurationSeconds);
        Assert.Equal(48_213_004, media.SizeBytes);
        Assert.Equal("hevc", media.Video!.Codec);
        Assert.Equal(3840, media.Video.Width);
        Assert.True(media.Video.IsHdr);
        Assert.Equal(10, media.Video.BitDepth);
        Assert.Equal("aac", media.Audio!.Codec);
        Assert.Equal("mp4a.40.2", Codecs.Audio(media.Audio));
    }

    [Fact]
    public void MediaInfo_FallsBackToTheLongestStream_AndHasNoVideoWithoutOne()
    {
        MediaInfo media = MediaInfo.Parse("""
            {"streams": [{"codec_type": "audio", "codec_name": "opus", "duration": "3.2"}], "format": {}}
            """);

        Assert.Equal(3.2, media.DurationSeconds);
        Assert.Null(media.Video);
        Assert.True(new VideoStreamInfo("h264", null, null, 1, 1, "yuv420p", "arib-std-b67", null, FrameRate.Default).IsHdr);
        Assert.False(new VideoStreamInfo("h264", null, null, 1, 1, "yuv420p", "bt709", null, FrameRate.Default).IsHdr);
    }

    [Fact]
    public void Settings_NeedTheApiAndALongEnoughSecret()
    {
        WorkerSettings valid = new() { ApiBaseUrl = "https://reelshelf.example", Secret = new string('s', 32) };
        Assert.Null(valid.Problem());

        Assert.NotNull(new WorkerSettings { Secret = new string('s', 32) }.Problem());
        Assert.NotNull(new WorkerSettings { ApiBaseUrl = "https://reelshelf.example", Secret = "short" }.Problem());
        Assert.NotNull(new WorkerSettings { ApiBaseUrl = "ftp://x", Secret = new string('s', 32) }.Problem());
        Assert.NotNull((new WorkerSettings { ApiBaseUrl = "https://x", Secret = new string('s', 32), Preset = 14 }).Problem());
        Assert.NotNull((new WorkerSettings { ApiBaseUrl = "https://x", Secret = new string('s', 32), Niceness = 20 }).Problem());
        Assert.NotNull((new WorkerSettings { ApiBaseUrl = "https://x", Secret = new string('s', 32), HighFrameRateBitrateKbps = 0 }).Problem());
    }

    [Fact]
    public void Backoff_DoublesUpToTheCeiling_AndResets()
    {
        Backoff backoff = new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), new Random(1));

        double[] delays = Enumerable.Range(0, 6).Select(_ => backoff.Next().TotalSeconds).ToArray();
        double[] expected = [5, 10, 20, 30, 30, 30];
        for (int index = 0; index < delays.Length; index++)
        {
            Assert.InRange(delays[index], expected[index] * 0.8, expected[index] * 1.2);
        }

        backoff.Reset();
        Assert.InRange(backoff.Next().TotalSeconds, 4, 6);
    }

    private static JsonElement Json(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
