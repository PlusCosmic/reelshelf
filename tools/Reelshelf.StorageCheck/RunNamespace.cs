using Reelshelf.Games;
using Reelshelf.Moderation;
using Reelshelf.Plans;
using Reelshelf.Video.R2;

namespace Reelshelf.StorageCheck;

/// <summary>
/// Every key one run may write, under ids made fresh for it. Clip, category, account and evidence ids are random v4
/// GUIDs in production too, so a fresh one can't name anything that exists, and every key below starts with one of
/// them. Nothing the run deletes is outside <see cref="OwnsMediaKey"/> and <see cref="OwnsEvidenceKey"/>.
/// </summary>
public sealed class RunNamespace
{
    /// <summary>The browser's upload, the worker's outputs, the download, the CDN and the purge.</summary>
    public Guid ClipId { get; } = Guid.NewGuid();

    /// <summary>The server's own uploads, reads, lists and the quarantine.</summary>
    public Guid ServerClipId { get; } = Guid.NewGuid();

    /// <summary>An upload left open on purpose, then aborted; also the missing keys read before anything exists.</summary>
    public Guid AbandonedClipId { get; } = Guid.NewGuid();

    /// <summary>The quarantine folder in the media bucket and the record's folder in the evidence bucket.</summary>
    public Guid EvidenceId { get; } = Guid.NewGuid();

    public Guid ExportUserId { get; } = Guid.NewGuid();
    public Guid ExportId { get; } = Guid.NewGuid();
    public Guid ExportLeaseId { get; } = Guid.NewGuid();
    public Guid GameCategoryId { get; } = Guid.NewGuid();

    /// <summary>Exact keys in the media bucket, which are deleted and have their open uploads aborted.</summary>
    public IReadOnlyList<string> MediaKeys =>
        [R2ObjectKeys.Upload(ClipId), R2ObjectKeys.Upload(ServerClipId), R2ObjectKeys.Upload(AbandonedClipId)];

    /// <summary>Folders in the media bucket, emptied and checked empty.</summary>
    public IReadOnlyList<string> MediaPrefixes =>
    [
        R2ObjectKeys.ClipPrefix(ClipId), R2ObjectKeys.ClipPrefix(ServerClipId), R2ObjectKeys.ClipPrefix(AbandonedClipId),
        EvidenceQuarantineKeys.Prefix(EvidenceId), ExportParts.AccountPrefix(ExportUserId),
        $"{GameArtKeys.Prefix}{GameCategoryId}/"
    ];

    public IReadOnlyList<string> EvidencePrefixes => [ClipEvidenceProcessor.Prefix(EvidenceId)];

    public bool OwnsMediaKey(string key) =>
        MediaKeys.Contains(key, StringComparer.Ordinal) ||
        MediaPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal));

    public bool OwnsEvidenceKey(string key) =>
        EvidencePrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>What to delete by hand if a run is killed before it can clean up.</summary>
    public IEnumerable<string> Describe()
    {
        yield return $"clip ids: {ClipId} (browser upload, worker, CDN), {ServerClipId} (server side), " +
                     $"{AbandonedClipId} (abandoned upload)";
        yield return $"evidence id {EvidenceId}, export account {ExportUserId}, game category {GameCategoryId}";
        yield return "media bucket keys: " + string.Join(", ", MediaKeys.Concat(MediaPrefixes.Select(prefix => prefix + "*")));
        yield return "evidence bucket keys: " + string.Join(", ", EvidencePrefixes.Select(prefix => prefix + "*"));
    }
}
