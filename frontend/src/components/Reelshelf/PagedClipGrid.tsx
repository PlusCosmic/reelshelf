import type { GameCategoryResponse } from "@/api-client";
import { LoadMore } from "@/components/ui";
import { useClipsInfinite, type ClipsFilters } from "@/hooks/clips.queries";
import { ClipGrid } from "./primitives/ClipViews";

/** Placeholder rows shaped like the filmstrip, shown while a page of clips is on its way. */
function SkeletonRows({ count }: { count: number }) {
  return (
    <div className="rs-filmstrip" aria-hidden="true">
      {Array.from({ length: count }, (_, index) => (
        <div className="rs-row rs-row-skeleton" key={index}>
          <span className="rs-skeleton rs-skeleton-thumb" />
          <span className="rs-row-body">
            <span className="rs-skeleton rs-skeleton-title" />
            <span className="rs-skeleton rs-skeleton-meta" />
          </span>
        </div>
      ))}
    </div>
  );
}

/**
 * A grid that pages through the clips endpoint. Filtering and searching happen server-side, so a
 * category larger than one page still matches on clips that have not been fetched yet.
 */
export function PagedClipGrid({
  filters,
  categories,
  variant = "poster",
  enabled = true,
  groupByMonth = false,
  showGame = true,
}: {
  filters: ClipsFilters;
  categories: GameCategoryResponse[];
  variant?: "poster" | "grid" | "filmstrip";
  enabled?: boolean;
  groupByMonth?: boolean;
  showGame?: boolean;
}) {
  const query = useClipsInfinite(filters, enabled);
  const clips = query.data?.pages.flatMap((page) => page.clips) ?? [];
  const rows = variant === "filmstrip";

  if (query.isLoading)
    return rows ? (
      <SkeletonRows count={4} />
    ) : (
      <div className="rs-empty">Loading clips…</div>
    );
  // A page that fails partway through keeps the clips already on screen; LoadMore offers the retry.
  if (query.isError && clips.length === 0)
    return <div className="rs-empty">Clips could not be loaded.</div>;

  return (
    <>
      <ClipGrid
        clips={clips}
        categories={categories}
        variant={variant}
        groupByMonth={groupByMonth}
        showGame={showGame}
      />
      {rows && query.isFetchingNextPage ? <SkeletonRows count={3} /> : null}
      <LoadMore label="clips" query={query} />
    </>
  );
}
