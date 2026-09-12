import type { GameCategoryResponse } from "@/api-client";
import { LoadMore } from "@/components/ui";
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
  const query = useClipsInfinite(filters, enabled);
  const clips = query.data?.pages.flatMap((page) => page.clips) ?? [];

  if (query.isLoading) return <div className="rs-empty">Loading clips…</div>;
  // A page that fails partway through keeps the clips already on screen; LoadMore offers the retry.
  if (query.isError && clips.length === 0)
    return <div className="rs-empty">Clips could not be loaded.</div>;

  return (
    <>
      <ClipGrid clips={clips} categories={categories} variant={variant} />
      <LoadMore label="clips" query={query} />
    </>
  );
}
