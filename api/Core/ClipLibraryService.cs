using Reelshelf.Core.Models;
using Reelshelf.Games;

namespace Reelshelf.Core;

public class ClipLibraryService(
    GameCategoryService gameCategoryService,
    ClipService clipService,
    ClipsStatements clipsStatements)
{
    private const int PreviewClipsPerCategory = 96;

    public async Task<ClipLibraryResponse> GetLibrary(Guid userId)
    {
        // Only the caller's categories: the global list grows with every account's custom categories.
        List<GameCategoryResponse> categories = await gameCategoryService.GetLibraryCategoriesAsync(userId);
        List<Clip> clips = [];

        foreach (GameCategoryResponse category in categories)
        {
            PagedClipsResponse categoryClips = await clipService.GetClipsForCategory(
                category.Id,
                userId,
                1,
                PreviewClipsPerCategory,
                sortOrder: ClipSortOrder.DateDescending);

            clips.AddRange(categoryClips.Clips);
        }

        // Totals come from the database, not from the clip list above: that list stops at
        // PreviewClipsPerCategory per category and skips categories no longer in the caller's library,
        // so summing it reports less than the storage meter does.
        List<ClipsStatements.CategoryTotalsRow> totalsRows = await clipsStatements.GetCategoryTotalsByOwner(userId);
        List<ClipCategoryTotals> categoryTotals = totalsRows
            .Select(row => new ClipCategoryTotals(row.GameCategoryId, row.ClipCount, row.DurationSeconds, row.StorageBytes))
            .ToList();
        ClipLibraryTotals totals = new(
            categoryTotals.Sum(total => total.ClipCount),
            categoryTotals.Sum(total => total.DurationSeconds),
            categoryTotals.Sum(total => total.StorageBytes));

        return new ClipLibraryResponse(categories, clips, totals, categoryTotals);
    }
}
