using FFMpegCore;
using Reelshelf.ClipTranscription;
using Xunit;

namespace Reelshelf.Test.ClipTranscription;

/// <summary>Runs the real ffmpeg, which CI installs, against generated clips.</summary>
public class ClipAudioExtractorTests
{
    [Fact]
    public async Task Extract_WritesMonoLowBitrateAac_WithoutTheVideo()
    {
        string clip = await GenerateClipAsync(withAudio: true);
        string output = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.m4a");
        try
        {
            ClipAudio audio = await ClipAudioExtractor.ExtractAsync(new Uri(clip), output, CancellationToken.None);

            Assert.True(audio.HasAudio);
            Assert.Equal(output, audio.Path);
            Assert.InRange(audio.Seconds, 2.9, 3.2);
            // 3 seconds at 32 kbps is about 12 KB; the 1080p video alone would be far larger.
            Assert.InRange(audio.Bytes, 1, 40_000);

            IMediaAnalysis analysis = await FFProbe.AnalyseAsync(output);
            Assert.Null(analysis.PrimaryVideoStream);
            Assert.Equal("aac", analysis.PrimaryAudioStream!.CodecName);
            Assert.Equal(1, analysis.PrimaryAudioStream.Channels);
            Assert.Equal(16000, analysis.PrimaryAudioStream.SampleRateHz);
        }
        finally
        {
            File.Delete(clip);
            File.Delete(output);
        }
    }

    [Fact]
    public async Task Extract_ReportsNoAudio_ForAVideoWithoutAnAudioTrack()
    {
        string clip = await GenerateClipAsync(withAudio: false);
        string output = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.m4a");
        try
        {
            ClipAudio audio = await ClipAudioExtractor.ExtractAsync(new Uri(clip), output, CancellationToken.None);

            Assert.False(audio.HasAudio);
            Assert.Null(audio.Path);
            Assert.False(File.Exists(output));
        }
        finally
        {
            File.Delete(clip);
        }
    }

    private static async Task<string> GenerateClipAsync(bool withAudio)
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.mp4");
        FFMpegArguments arguments = FFMpegArguments.FromFileInput(
            "testsrc2=size=1920x1080:duration=3", verifyExists: false, options => options.ForceFormat("lavfi"));
        if (withAudio)
        {
            arguments = arguments.AddFileInput(
                "sine=frequency=440:duration=3", verifyExists: false, options => options.ForceFormat("lavfi"));
        }

        await arguments.OutputToFile(path, overwrite: true).ProcessAsynchronously();
        return path;
    }
}
