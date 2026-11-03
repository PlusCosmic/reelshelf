import type { ClipsFilters } from "@/hooks/clips.queries";

/**
 * The library list's filters as they appear in its URL (`/?q=…` or `/games/$slug?tag=…`), so the back button, a
 * reload or a bookmark brings the same list back. Empty filters are left out of the URL.
 */
export type LibrarySearch = {
  q?: string;
  tag?: string;
  legend?: string;
};

/**
 * What a clip's link carries about the list it was opened from: that list's filters, and `from: "library"` when it
 * was the whole library rather than the clip's own book. The clip page uses it to go back to that list and to step
 * through it.
 */
export type ClipListSearch = LibrarySearch & {
  from?: "library";
  /** Sorting through the list: keep or bin each clip in turn. */
  sort?: boolean;
};

const text = (value: unknown) =>
  typeof value === "string" && value.trim() !== "" ? value : undefined;

/** Reads the library's filters from a URL's search, dropping anything else. */
export function readLibrarySearch(
  search: Record<string, unknown>,
): LibrarySearch {
  return withoutEmpty({
    q: text(search.q),
    tag: text(search.tag),
    legend: text(search.legend),
  });
}

/** Reads a clip page's list context from its URL's search. */
export function readClipListSearch(
  search: Record<string, unknown>,
): ClipListSearch {
  return withoutEmpty({
    ...readLibrarySearch(search),
    from: search.from === "library" ? "library" : undefined,
    sort: search.sort === true || search.sort === "true" ? true : undefined,
  });
}

/** Leaves unset keys out, so the URL never carries `?q=` with nothing after it. */
export function withoutEmpty<T extends Record<string, unknown>>(value: T): T {
  return Object.fromEntries(
    Object.entries(value).filter(([, entry]) => entry !== undefined),
  ) as T;
}

/** The list context a clip opened from this list carries. */
export function clipListSearchFor(
  search: LibrarySearch,
  inBook: boolean,
): ClipListSearch {
  return withoutEmpty({
    ...search,
    from: inBook ? undefined : "library",
  });
}

/** The library's own filters back out of a clip's list context. */
export function librarySearchOf(list: ClipListSearch): LibrarySearch {
  return withoutEmpty({ q: list.q, tag: list.tag, legend: list.legend });
}

/**
 * The API filters for a clip's list: its book's clips (or every clip when it came from the whole library), with the
 * list's search, tag and legend.
 */
export function listFiltersFor(
  list: ClipListSearch,
  categoryId: string | undefined,
): ClipsFilters {
  return {
    categoryId: list.from === "library" ? null : (categoryId ?? null),
    search: list.q,
    tag: list.tag ?? null,
    legend: list.legend ?? null,
  };
}

/** Whether a list has any filter set, so it says "these clips" rather than every clip. */
export function hasFilters(search: LibrarySearch) {
  return !!(search.q || search.tag || search.legend);
}
