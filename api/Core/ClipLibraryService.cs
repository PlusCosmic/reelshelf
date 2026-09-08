using Reelshelf.Core.Models;
using Reelshelf.Games;

namespace Reelshelf.Core;

public class ClipLibraryService(
    GameCategoryService gameCategoryService,
    ClipService clipService)
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

        return new ClipLibraryResponse(categories, clips);
    }
}
