using Reelshelf.Uploader.Core.Copy;
using Reelshelf.Uploader.Core.Ledger;
using Reelshelf.Uploader.Core.Matching;

namespace Reelshelf.Uploader.Core.State;

/// <summary>One row of S4, recent uploads: the clip's title, where it is, and its book, length, size and time.</summary>
public sealed record RecentUpload(
    LedgerEntry Entry,
    string Title,
    string Status,
    string Meta,
    string? ClothColor,
    bool CanTryAgain,
    string? BookSlug)
{
    /// <summary>Clicking a clip on the shelf opens it on reelshelf.app.</summary>
    public bool CanOpen => Entry.State == LedgerState.Shelved && Entry.ClipId is not null && BookSlug is not null;

    public static RecentUpload From(LedgerEntry entry, IReadOnlyList<Book> books, DateTimeOffset now,
        int? currentPercent = null, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        Book? book = books.FirstOrDefault(candidate => candidate.Id == entry.CategoryId);
        bool retrying = entry.Attempts > 0 && entry.NextAttemptAt is { } next && next > now;
        string status = entry.State switch
        {
            LedgerState.Shelved => UploaderCopy.Recent.OnShelf,
            LedgerState.Duplicate => UploaderCopy.Recent.Duplicate,
            LedgerState.TooLong => UploaderCopy.Recent.TooLong,
            LedgerState.Failed => UploaderCopy.Recent.Failed,
            LedgerState.NeedsBook => UploaderCopy.Recent.NeedsBook,
            LedgerState.PlanFull => UploaderCopy.Recent.LibraryFull,
            _ when retrying => UploaderCopy.Recent.RetryingSoon,
            LedgerState.Uploading or LedgerState.Waiting when currentPercent is { } percent =>
                UploaderCopy.Recent.Uploading(percent),
            _ => UploaderCopy.Recent.Waiting
        };
        DateTimeOffset? shelvedAt = entry.State == LedgerState.Shelved && entry.ShelvedAt is { } at
            ? TimeZoneInfo.ConvertTime(at, zone)
            : null;
        return new RecentUpload(entry, entry.Title, status,
            UploaderCopy.Recent.Meta(book?.Name ?? UploaderCopy.Recent.NoBook, entry.LengthSeconds, entry.Size, shelvedAt),
            book?.ClothColor, entry.State == LedgerState.Failed, book?.Slug);
    }
}
