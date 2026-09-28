using System.Text;

namespace Reelshelf.FFmpeg;

/// <summary>What a downloaded clip is saved as: its title, made safe for a file name on any system.</summary>
public static class DownloadFileName
{
    private const int MaxLength = 150;
    private static readonly char[] Forbidden = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    public static string For(string? title, Guid videoId)
    {
        StringBuilder name = new();
        foreach (char character in title ?? "")
        {
            name.Append(char.IsControl(character) || Forbidden.Contains(character) ? ' ' : character);
        }

        string cleaned = string.Join(' ', name.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim(' ', '.');
        // Recorder titles often keep their original extension; don't end up with "clip.mp4.mp4".
        if (cleaned.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned[..^4].TrimEnd(' ', '.');
        }

        if (cleaned.Length > MaxLength)
        {
            cleaned = cleaned[..MaxLength].TrimEnd(' ', '.');
        }

        return (cleaned.Length == 0 ? videoId.ToString() : cleaned) + ".mp4";
    }
}
