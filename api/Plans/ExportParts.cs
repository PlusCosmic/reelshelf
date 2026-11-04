using Reelshelf.FFmpeg;

namespace Reelshelf.Plans;

/// <summary>How an export's clips are laid out: which zip each goes in, and its name there.</summary>
public static class ExportParts
{
    /// <summary>
    /// The most one zip holds. Parts stay under 4 GiB so each is a plain zip any unzipper reads, without Zip64, and a
    /// download that fails costs at most one part.
    /// </summary>
    public const long DefaultMaxPartBytes = 4L * 1024 * 1024 * 1024 - 64L * 1024 * 1024;

    /// <summary>What a clip whose size isn't known is assumed to take: a 120 s clip at 60 fps.</summary>
    public const long UnknownClipBytes = 100L * 1024 * 1024;

    /// <summary>
    /// Splits <paramref name="clips"/>, in order, into parts of at most <paramref name="maxPartBytes"/> by their
    /// <see cref="ExportClipRow.StorageSize"/>. A clip larger than a part on its own gets a part of its own.
    /// </summary>
    public static List<List<ExportClipRow>> Plan(IReadOnlyList<ExportClipRow> clips, long maxPartBytes)
    {
        List<List<ExportClipRow>> parts = [];
        List<ExportClipRow> current = [];
        long currentBytes = 0;
        foreach (ExportClipRow clip in clips)
        {
            long size = clip.StorageSize is > 0 ? clip.StorageSize.Value : UnknownClipBytes;
            if (current.Count > 0 && currentBytes + size > maxPartBytes)
            {
                parts.Add(current);
                current = [];
                currentBytes = 0;
            }

            current.Add(clip);
            currentBytes += size;
        }

        if (current.Count > 0)
        {
            parts.Add(current);
        }

        return parts;
    }

    /// <summary>
    /// The clip's file name in the zip: its date and game, then its title (<c>2026-10-23 Apex Legends - Triple.mp4</c>),
    /// made unique within the export with " (2)", " (3)" and so on.
    /// </summary>
    public static string EntryName(ExportClipRow clip, ISet<string> taken)
    {
        string game = string.IsNullOrWhiteSpace(clip.GameName) ? "" : $"{clip.GameName} - ";
        string title = string.IsNullOrWhiteSpace(clip.Title) ? "Clip" : clip.Title;
        string stem = Path.GetFileNameWithoutExtension(
            DownloadFileName.For($"{clip.CreatedAt:yyyy-MM-dd} {game}{title}", clip.Id));
        string name = $"{stem}.mp4";
        for (int copy = 2; !taken.Add(name.ToLowerInvariant()); copy++)
        {
            name = $"{stem} ({copy}).mp4";
        }

        return name;
    }

    public static string AccountPrefix(Guid userId) => $"exports/{userId}/";

    public static string Prefix(Guid userId, Guid exportId) => $"{AccountPrefix(userId)}{exportId}/";

    /// <summary>
    /// Where one run of an export writes its parts: <c>exports/{userId}/{exportId}/{leaseId}/</c>, so a run that lost
    /// its lease never overwrites or deletes another run's parts. Exports made before leases (V58) have none, and
    /// their parts are directly under the export's prefix.
    /// </summary>
    public static string RunPrefix(ClipExport export) =>
        export.LeaseId is { } lease ? $"{Prefix(export.UserId, export.Id)}{lease}/" : Prefix(export.UserId, export.Id);

    public static string FileName(int index) => $"reelshelf-clips-{index}.zip";

    /// <summary>A part of an export made before leases (V58).</summary>
    public static string Key(Guid userId, Guid exportId, int index) => Prefix(userId, exportId) + FileName(index);

    /// <summary>A part written by <paramref name="export"/>'s run, under its lease.</summary>
    public static string Key(ClipExport export, int index) => RunPrefix(export) + FileName(index);
}
