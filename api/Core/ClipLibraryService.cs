using Reelshelf.Core.Models;
using Reelshelf.Games;

namespace Reelshelf.Core;

/// <summary>
/// The library shell: the caller's categories and the totals the shelf and topline render.
/// Clips are not included — the grids page through the clips endpoint, so this stays one query per
/// concern instead of a per-category fan-out that could only ever return a preview anyway.
/// </summary>
public class ClipLibraryService(
    GameCategoryService gameCategoryService,
    ClipsStatements clipsStatements)
{
    public async Task<ClipLibraryResponse> GetLibrary(Guid userId)
    {
        // Only the caller's categories: the global list grows with every account's custom categories.
        List<GameCategoryResponse> categories = await gameCategoryService.GetLibraryCategoriesAsync(userId);

        List<ClipsStatements.CategoryTotalsRow> totalsRows = await clipsStatements.GetCategoryTotalsByOwner(userId);
        List<ClipCategoryTotals> categoryTotals = totalsRows
            .Select(row => new ClipCategoryTotals(
                row.GameCategoryId,
                row.ClipCount,
                row.UnviewedCount,
                row.DurationSeconds,
                row.StorageBytes))
            .ToList();

        // Summed over every category the owner has clips in, including any dropped from their library,
        // so the topline matches the storage meter rather than the shelf.
        ClipLibraryTotals totals = new(
            categoryTotals.Sum(total => total.ClipCount),
            categoryTotals.Sum(total => total.UnviewedCount),
            categoryTotals.Sum(total => total.DurationSeconds),
            categoryTotals.Sum(total => total.StorageBytes));

        return new ClipLibraryResponse(categories, totals, categoryTotals);
    }
}
