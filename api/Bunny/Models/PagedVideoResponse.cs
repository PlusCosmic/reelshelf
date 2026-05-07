namespace Nucleus.Clips.Bunny.Models;

public record PagedVideoResponse(long TotalItems, long CurrentPage, long ItemsPerPage, List<BunnyVideo> Items)
{
}
