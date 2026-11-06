using System.Text;
using System.Text.RegularExpressions;

namespace Reelshelf.Uploader.Core.Matching;

/// <summary>A book on the account's shelf, as matching needs it.</summary>
public sealed record Book(Guid Id, string Name, string? Slug, string? ClothColor = null);

/// <summary>How a file's book was found, as the frontend's <c>GameAssignmentSource</c>.</summary>
public enum BookSource
{
    /// <summary>A name in the path matched the book.</summary>
    SourcePath,

    /// <summary>Nothing matched, so the fallback (the default book) was used.</summary>
    Fallback,

    /// <summary>The person chose the book for this folder.</summary>
    Saved
}

public sealed record BookMatch(Guid? CategoryId, BookSource? Source);

/// <summary>
/// Which book a clip goes on. A port of the frontend's <c>assignGame</c> (<c>frontend/src/hooks/bulkUploadQueue.ts</c>)
/// with its <c>normalizeForMatch</c> rule: lower case, anything but letters and digits as one space, matched as a
/// whole phrase or, with the spaces taken out, anywhere; the longest matching name wins. The uploader matches the
/// first folder below the recordings folder (<see cref="Resolve"/>).
/// </summary>
public static partial class BookMatcher
{
    public static BookMatch AssignGame(string sourcePath, IReadOnlyList<Book> books, Guid? fallbackCategoryId = null)
    {
        string normalizedPath = NormalizeForMatch(sourcePath);
        string pathCompact = CompactForMatch(sourcePath);

        Book? best = null;
        int bestScore = 0;
        foreach (Book book in books)
        {
            int score = 0;
            foreach (string? value in new[] { book.Name, book.Slug })
            {
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                string normalizedValue = NormalizeForMatch(value);
                string compactValue = CompactForMatch(value);
                bool matched = ContainsPhrase(normalizedPath, normalizedValue) ||
                               (compactValue.Length > 0 && pathCompact.Contains(compactValue, StringComparison.Ordinal));
                if (matched)
                {
                    score = Math.Max(score, compactValue.Length);
                }
            }

            if (score > 0 && (best is null || score > bestScore))
            {
                best = book;
                bestScore = score;
            }
        }

        if (best is not null)
        {
            return new BookMatch(best.Id, BookSource.SourcePath);
        }

        return fallbackCategoryId is { } fallback
            ? new BookMatch(fallback, BookSource.Fallback)
            : new BookMatch(null, null);
    }

    /// <summary>
    /// The book for a file in <paramref name="folder"/> (the first folder below the recordings folder, "" for the folder
    /// itself): the book saved for that folder, else the book its name matches, else the default book, else none, and
    /// the file waits as <c>needs_book</c>. A saved or default book no longer on the shelf counts as none.
    /// </summary>
    public static BookMatch Resolve(string folder, IReadOnlyList<Book> books, Guid? savedBook, Guid? defaultBook)
    {
        bool OnShelf(Guid id) => books.Any(book => book.Id == id);

        if (savedBook is { } saved && OnShelf(saved))
        {
            return new BookMatch(saved, BookSource.Saved);
        }

        Guid? fallback = defaultBook is { } chosen && OnShelf(chosen) ? chosen : null;
        return folder.Length == 0
            ? fallback is { } rootBook ? new BookMatch(rootBook, BookSource.Fallback) : new BookMatch(null, null)
            : AssignGame(folder, books, fallback);
    }

    public static string NormalizeForMatch(string value)
    {
        return $" {NonAlphanumeric().Replace(value.ToLowerInvariant(), " ").Trim()} ";
    }

    public static string CompactForMatch(string value)
    {
        StringBuilder compact = new(value.Length);
        foreach (char c in value.ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                compact.Append(c);
            }
        }

        return compact.ToString();
    }

    private static bool ContainsPhrase(string normalizedPath, string normalizedValue) =>
        normalizedValue.Trim().Length > 0 && normalizedPath.Contains(normalizedValue, StringComparison.Ordinal);

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumeric();
}
