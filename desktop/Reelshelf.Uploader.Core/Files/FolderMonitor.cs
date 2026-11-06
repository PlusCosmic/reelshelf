using Reelshelf.Uploader.Core.Ledger;
using Reelshelf.Uploader.Core.Matching;
using Reelshelf.Uploader.Core.Platform;
using Reelshelf.Uploader.Core.Settings;

namespace Reelshelf.Uploader.Core.Files;

/// <summary>
/// Turns what is in the recordings folder into ledger rows: files there when the folder is chosen become
/// <c>baseline</c>, and every other file the ledger doesn't know (by path, size and last-write time) is watched until
/// it settles, then added as <c>waiting</c> on its book, or <c>needs_book</c>. It runs no timers itself: the engine
/// calls <see cref="Rescan"/> every 60 s and <see cref="Settle"/> every 2 s.
/// </summary>
public sealed class FolderMonitor(UploadLedger ledger, IClock clock, Func<string, bool>? canOpen = null)
{
    private FileSettler? _settler;

    public string? Root { get; private set; }

    public int Settling => _settler?.Count ?? 0;

    /// <summary>Starts watching a folder. Its files are baselined only when <paramref name="baseline"/> is set (S2).</summary>
    public int Use(string root, bool baseline)
    {
        Root = Path.GetFullPath(root);
        _settler = new FileSettler(Root, clock, canOpen);
        return baseline ? ledger.AddBaseline(Root, FolderScanner.Scan(Root), clock.UtcNow) : 0;
    }

    /// <summary>Looks over the whole folder for files the ledger doesn't have.</summary>
    public void Rescan()
    {
        if (Root is null || _settler is null)
        {
            return;
        }

        foreach (ScannedFile file in FolderScanner.Scan(Root))
        {
            if (!ledger.Knows(Root, file))
            {
                _settler.Track(file.RelativePath);
            }
        }
    }

    /// <summary>A watcher event: the file is looked at on the next <see cref="Settle"/> unless the ledger has it.</summary>
    public void MarkDirty(string relativePath)
    {
        if (Root is null || _settler is null || FolderScanner.Stat(Root, relativePath) is not { } file ||
            ledger.Knows(Root, file))
        {
            return;
        }

        _settler.Track(file.RelativePath);
    }

    /// <summary>Adds the files that have settled to the ledger, each on its book, and answers them.</summary>
    public List<LedgerEntry> Settle(IReadOnlyList<Book> books, UploaderSettings settings)
    {
        List<LedgerEntry> added = [];
        if (Root is null || _settler is null)
        {
            return added;
        }

        DateTimeOffset now = clock.UtcNow;
        foreach (ScannedFile file in _settler.Check())
        {
            if (ledger.Knows(Root, file))
            {
                continue;
            }

            BookMatch match = BookMatcher.Resolve(file.Folder, books, settings.SavedBookFor(file.Folder),
                settings.DefaultBookId);
            LedgerEntry entry = new()
            {
                Root = Root,
                Path = file.RelativePath,
                Size = file.Size,
                LastWriteUtc = file.LastWriteUtc,
                State = match.CategoryId is null ? LedgerState.NeedsBook : LedgerState.Waiting,
                CategoryId = match.CategoryId,
                CreatedAt = now,
                UpdatedAt = now
            };
            ledger.Save(entry);
            added.Add(entry);
        }

        return added;
    }

    /// <summary>
    /// Files waiting for a book whose folder now has one (a choice was saved, a default was set, or a game was added
    /// on the site and its name matches) go in the queue. Answers how many did.
    /// </summary>
    public int Rematch(IReadOnlyList<Book> books, UploaderSettings settings)
    {
        if (Root is null)
        {
            return 0;
        }

        int matched = 0;
        foreach (LedgerEntry entry in ledger.InState(Root, LedgerState.NeedsBook))
        {
            BookMatch match = BookMatcher.Resolve(entry.Folder, books, settings.SavedBookFor(entry.Folder),
                settings.DefaultBookId);
            if (match.CategoryId is not { } book)
            {
                continue;
            }

            entry.CategoryId = book;
            entry.State = LedgerState.Waiting;
            entry.UpdatedAt = clock.UtcNow;
            ledger.Save(entry);
            matched++;
        }

        return matched;
    }
}
