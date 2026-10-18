using System.Diagnostics;
using Xunit;

namespace Reelshelf.Worker.Test;

/// <summary>
/// A test that encodes with the real ffmpeg. It runs when the ffmpeg on the PATH has libsvtav1 and libopus (Ubuntu's
/// does, so CI's does) and is skipped otherwise.
/// </summary>
public sealed class SvtAv1FactAttribute : FactAttribute
{
    private static readonly Lazy<string?> Missing = new(FindMissing);

    public SvtAv1FactAttribute()
    {
        if (Missing.Value is { } missing)
        {
            Skip = missing;
        }
    }

    private static string? FindMissing()
    {
        try
        {
            using Process process = Process.Start(new ProcessStartInfo("ffmpeg", "-hide_banner -encoders")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            })!;
            string encoders = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (!encoders.Contains(" libsvtav1 ", StringComparison.Ordinal) ||
                !encoders.Contains(" libopus ", StringComparison.Ordinal))
            {
                return "ffmpeg on the PATH has no libsvtav1 or libopus encoder";
            }

            return null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return "ffmpeg is not installed";
        }
    }
}
