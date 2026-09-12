import { useEffect, useRef, useState } from "react";

/** Start the next page this far before the sentinel reaches the viewport, so scrolling rarely stalls. */
const ROOT_MARGIN = "600px 0px";

/** The parts of a TanStack infinite query this needs; any `useInfiniteQuery` result satisfies it. */
export type InfiniteScrollTarget = {
  hasNextPage: boolean;
  isFetchingNextPage: boolean;
  isFetchNextPageError: boolean;
  fetchNextPage: () => unknown;
};

/**
 * Fetches the next page as the end of the list nears the viewport. Put `sentinelRef` on an element
 * after the last item. A failed page stops the auto-loading, so a broken endpoint is asked once
 * rather than on every scroll; `autoLoads` is false where IntersectionObserver is missing, which
 * lets callers fall back to a button.
 */
export function useInfiniteScroll({
  hasNextPage,
  isFetchingNextPage,
  isFetchNextPageError,
  fetchNextPage,
}: InfiniteScrollTarget) {
  const [sentinel, setSentinel] = useState<HTMLElement | null>(null);
  const fetchNextPageRef = useRef(fetchNextPage);
  const autoLoads = typeof IntersectionObserver !== "undefined";

  useEffect(() => {
    fetchNextPageRef.current = fetchNextPage;
  }, [fetchNextPage]);

  useEffect(() => {
    if (!sentinel || !autoLoads) return;
    if (!hasNextPage || isFetchingNextPage || isFetchNextPageError) return;

    // Rebuilt after every page on purpose: an observer reports where the sentinel is as soon as it
    // starts watching, so a page too short to push the sentinel off screen loads the one after it.
    const observer = new IntersectionObserver(
      (entries) => {
        if (entries.some((entry) => entry.isIntersecting)) {
          fetchNextPageRef.current();
        }
      },
      { rootMargin: ROOT_MARGIN },
    );
    observer.observe(sentinel);
    return () => observer.disconnect();
  }, [
    autoLoads,
    hasNextPage,
    isFetchingNextPage,
    isFetchNextPageError,
    sentinel,
  ]);

  return { sentinelRef: setSentinel, autoLoads };
}
