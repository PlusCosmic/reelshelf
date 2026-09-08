import type { GameCategoryResponse } from "@/api-client";
import { useClipsInfinite, type ClipsFilters } from "@/hooks/clips.queries";
import { ClipGrid } from "./primitives/ClipViews";

/**
 * A grid that pages through the clips endpoint. Filtering and searching happen server-side, so a
 * category larger than one page still matches on clips that have not been fetched yet.
 */
export function PagedClipGrid({
  filters,
  categories,
  variant = "poster",
  enabled = true,
}: {
  filters: ClipsFilters;
  categories: GameCategoryResponse[];
  variant?: "poster" | "grid" | "filmstrip";
  enabled?: boolean;
}) {
  const {
    data,
    isLoading,
    isError,
    hasNextPage,
    isFetchingNextPage,
    fetchNextPage,
  } = useClipsInfinite(filters, enabled);

  if (isLoading) return <div className="rs-empty">Loading clips…</div>;
  if (isError)
    return <div className="rs-empty">Clips could not be loaded.</div>;

  const clips = data?.pages.flatMap((page) => page.clips) ?? [];

  return (
    <>
      <ClipGrid clips={clips} categories={categories} variant={variant} />
      {hasNextPage ? (
        <div className="rs-load-more">
          <button
            className="rs-chip"
            type="button"
            disabled={isFetchingNextPage}
            onClick={() => fetchNextPage()}
          >
            {isFetchingNextPage ? "Loading…" : "Load more clips"}
          </button>
        </div>
      ) : null}
    </>
  );
}
