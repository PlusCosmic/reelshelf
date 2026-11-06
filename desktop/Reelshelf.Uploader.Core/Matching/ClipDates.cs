using System.Text.RegularExpressions;

namespace Reelshelf.Uploader.Core.Matching;

/// <summary>
/// When a clip was made, as the frontend's <c>deriveCreatedAt</c> and <c>parseTimestampFromSourcePath</c>
/// (<c>frontend/src/hooks/bulkUploadQueue.ts</c>) work it out: the file's last-write time, else a timestamp in its
/// name, read as local time, else the epoch.
/// </summary>
public static partial class ClipDates
{
    public static DateTimeOffset DeriveCreatedAt(DateTimeOffset lastWriteUtc, string sourcePath, TimeZoneInfo? zone = null)
    {
        if (lastWriteUtc > DateTimeOffset.UnixEpoch)
        {
            return lastWriteUtc;
        }

        return ParseTimestampFromSourcePath(sourcePath, zone) ?? DateTimeOffset.UnixEpoch;
    }

    public static DateTimeOffset? ParseTimestampFromSourcePath(string sourcePath, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        foreach (Regex pattern in (Regex[])[DashedDateTime(), DashedDateDashedTime(), CompactDateTime()])
        {
            Match match = pattern.Match(sourcePath);
            if (!match.Success)
            {
                continue;
            }

            int year = int.Parse(match.Groups["year"].Value);
            int month = int.Parse(match.Groups["month"].Value);
            int day = int.Parse(match.Groups["day"].Value);
            int hour = int.Parse(match.Groups["hour"].Value);
            int minute = int.Parse(match.Groups["minute"].Value);
            int second = match.Groups["second"].Success ? int.Parse(match.Groups["second"].Value) : 0;

            // The frontend builds a Date and checks it didn't roll over (a 31 February); here that is a range check.
            if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(Math.Clamp(year, 1, 9999), month) ||
                hour > 23 || minute > 59 || second > 59)
            {
                continue;
            }

            DateTime local = new(year, month, day, hour, minute, second, DateTimeKind.Unspecified);
            if (zone.IsInvalidTime(local))
            {
                continue;
            }

            return new DateTimeOffset(local, zone.GetUtcOffset(local));
        }

        return null;
    }

    [GeneratedRegex(@"(?<year>[0-9]{4})-(?<month>[0-9]{2})-(?<day>[0-9]{2})[_\-\s](?<hour>[0-9]{2})(?<minute>[0-9]{2})")]
    private static partial Regex DashedDateTime();

    [GeneratedRegex(@"(?<year>[0-9]{4})-(?<month>[0-9]{2})-(?<day>[0-9]{2})[_\-\s](?<hour>[0-9]{2})-(?<minute>[0-9]{2})")]
    private static partial Regex DashedDateDashedTime();

    [GeneratedRegex(@"(?<year>[0-9]{4})(?<month>[0-9]{2})(?<day>[0-9]{2})[_\-\s]?(?<hour>[0-9]{2})(?<minute>[0-9]{2})(?<second>[0-9]{2})")]
    private static partial Regex CompactDateTime();
}
