using FFMpegCore;
using FFMpegCore.Enums;

namespace Reelshelf.ClipTranscription;

/// <summary>A clip's audio written to disk for upload, or a note that the video has no audio track.</summary>
public sealed record ClipAudio(bool HasAudio, string? Path, long Bytes, double Seconds);

/// <summary>
/// Pulls only the audio out of a clip with ffmpeg, as mono 16 kHz AAC at 32 kbps. Speech models need no more, and
/// at that rate the 25 MB upload limit holds about 100 minutes, so clips are never split.
/// </summary>
public static class ClipAudioExtractor
{
    public static async Task<ClipAudio> ExtractAsync(Uri input, string outputPath, CancellationToken cancellationToken)
    {
        IMediaAnalysis analysis = await FFProbe.AnalyseAsync(input, cancellationToken: cancellationToken);
        if (analysis.PrimaryAudioStream is null)
        {
            return new ClipAudio(false, null, 0, analysis.Duration.TotalSeconds);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await FFMpegArguments
            .FromUrlInput(input)
            .OutputToFile(outputPath, overwrite: true, options => options
                .DisableChannel(Channel.Video)
                .WithAudioCodec(AudioCodec.Aac)
                .WithAudioBitrate(32)
                .WithAudioSamplingRate(16000)
                .WithCustomArgument("-ac 1")
                .ForceFormat("mp4"))
            .CancellableThrough(cancellationToken)
            .ProcessAsynchronously();

        return new ClipAudio(true, outputPath, new FileInfo(outputPath).Length, analysis.Duration.TotalSeconds);
    }
}
