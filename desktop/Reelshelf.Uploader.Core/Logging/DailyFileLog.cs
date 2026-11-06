using System.Globalization;
using System.Text.RegularExpressions;

namespace Reelshelf.Uploader.Core.Logging;

/// <summary>
/// The uploader's log: one file a day, <c>logs\uploader-YYYYMMDD.log</c>, kept for <see cref="KeepFor"/>. Callers write
/// what happened, never a token, a sign-in code or a file's contents; <see cref="Redact"/> takes out anything shaped
/// like a device token in case one slips through.
/// </summary>
public sealed partial class DailyFileLog(string directory, Func<DateTimeOffset>? now = null)
{
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(14);

    private readonly Func<DateTimeOffset> _now = now ?? (() => DateTimeOffset.Now);
    private readonly Lock _lock = new();
    private DateOnly? _cleanedOn;

    public string Directory { get; } = directory;

    public string PathFor(DateTimeOffset day) =>
        Path.Combine(Directory, $"uploader-{day.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.log");

    public void Write(string message)
    {
        DateTimeOffset now = _now();
        string line = $"{now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture)} {Redact(message)}{Environment.NewLine}";
        lock (_lock)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.AppendAllText(PathFor(now), line);
                DateOnly today = DateOnly.FromDateTime(now.DateTime);
                if (_cleanedOn != today)
                {
                    _cleanedOn = today;
                    DeleteOld(now);
                }
            }
            catch (IOException)
            {
                // Logging never stops the uploader.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>Deletes the daily files older than <see cref="KeepFor"/>.</summary>
    public void DeleteOld(DateTimeOffset now)
    {
        DateOnly oldestKept = DateOnly.FromDateTime(now.DateTime).AddDays(-(int)KeepFor.TotalDays + 1);
        foreach (string file in System.IO.Directory.EnumerateFiles(Directory, "uploader-*.log"))
        {
            Match match = DailyFile().Match(Path.GetFileName(file));
            if (match.Success &&
                DateOnly.TryParseExact(match.Groups[1].Value, "yyyyMMdd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateOnly day) &&
                day < oldestKept)
            {
                File.Delete(file);
            }
        }
    }

    public static string Redact(string message) => Token().Replace(message, "rsd_[redacted]");

    [GeneratedRegex(@"^uploader-(\d{8})\.log$")]
    private static partial Regex DailyFile();

    [GeneratedRegex(@"rsd_[A-Za-z0-9_\-]+")]
    private static partial Regex Token();
}
