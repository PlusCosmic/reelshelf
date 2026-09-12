import {
  useInfiniteScroll,
  type InfiniteScrollTarget,
} from "@/hooks/useInfiniteScroll";
import { Button } from "./Button";
import { cx } from "./cx";

/**
 * The tail of a paged list: it loads the next page on its own as the reader reaches it, and only
 * offers a button where that cannot happen — after a failed page, or without IntersectionObserver.
 */
export function LoadMore({
  className,
  label,
  query,
}: {
  className?: string;
  /** Plural noun for the items, as in "Load more clips". */
  label: string;
  query: InfiniteScrollTarget;
}) {
  const { hasNextPage, isFetchingNextPage, isFetchNextPageError } = query;
  const { sentinelRef, autoLoads } = useInfiniteScroll(query);

  if (!hasNextPage) return null;

  return (
    <div className={cx("rs-load-more", className)} ref={sentinelRef}>
      {isFetchNextPageError ? (
        <>
          <span className="rs-load-more-error">
            More {label} could not be loaded.
          </span>
          <Button onClick={() => query.fetchNextPage()}>Try again</Button>
        </>
      ) : isFetchingNextPage || autoLoads ? (
        <span className="rs-spinner" aria-hidden="true" />
      ) : (
        <Button onClick={() => query.fetchNextPage()}>Load more {label}</Button>
      )}
      <span className="rs-visually-hidden" role="status">
        {isFetchingNextPage ? `Loading more ${label}…` : ""}
      </span>
    </div>
  );
}
