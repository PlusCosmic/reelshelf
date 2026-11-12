import type { ClipSearchFilters, ClipSearchInterpretation } from "@/api-client";
import { clipSearchCopy } from "@/copy/clipSearch";

/**
 * A search by description: the words typed, and once the API has read them, the filters it read them into. Sending the
 * filters back (with some taken away) searches again without asking the model to read the words a second time.
 */
export type ClipSearchState = {
  query: string;
  filters: ClipSearchFilters | null;
};

/** One filter a search was read into, shown as a chip that takes it away. */
export type ClipSearchChip = {
  key: string;
  label: string;
  /** The filters without this one. */
  without: ClipSearchFilters;
};

/** The interpretation as filters to send back. */
export function filtersOf(
  interpretation: ClipSearchInterpretation,
): ClipSearchFilters {
  return {
    startDate: interpretation.startDate,
    endDate: interpretation.endDate,
    gameCategoryIds: interpretation.games.map((game) => game.id),
    moodTags: [...interpretation.moodTags],
    people: [...interpretation.people],
    phrase: interpretation.phrase,
  };
}

const isFirstOfMonth = (date: Date) => date.getUTCDate() === 1;
const isLastOfMonth = (date: Date) =>
  new Date(
    Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), date.getUTCDate() + 1),
  ).getUTCDate() === 1;
const sameDay = (a: Date, b: Date) =>
  a.toISOString().slice(0, 10) === b.toISOString().slice(0, 10);

/** The dates a search was read into, in the fewest words: "2025", "October 2026", "3 Mar 2026", or a range. */
export function dateLabel(start: Date | null, end: Date | null): string | null {
  const chips = clipSearchCopy.chips;
  if (start && end) {
    if (sameDay(start, end)) return chips.day(start);
    const sameYear = start.getUTCFullYear() === end.getUTCFullYear();
    if (
      sameYear &&
      start.getUTCMonth() === 0 &&
      isFirstOfMonth(start) &&
      end.getUTCMonth() === 11 &&
      end.getUTCDate() === 31
    )
      return chips.year(start.getUTCFullYear());
    if (
      sameYear &&
      start.getUTCMonth() === end.getUTCMonth() &&
      isFirstOfMonth(start) &&
      isLastOfMonth(end)
    )
      return chips.month(start);
    return chips.range(start, end);
  }
  if (start) return chips.since(start);
  if (end) return chips.until(end);
  return null;
}

/** The chips for what a search was read into, in the order they read: when, the games, the mood, who, then what. */
export function chipsFor(
  interpretation: ClipSearchInterpretation,
): ClipSearchChip[] {
  const filters = filtersOf(interpretation);
  const chips: ClipSearchChip[] = [];
  const dates = dateLabel(filters.startDate, filters.endDate);
  if (dates)
    chips.push({
      key: "dates",
      label: dates,
      without: { ...filters, startDate: null, endDate: null },
    });
  for (const game of interpretation.games)
    chips.push({
      key: `game:${game.id}`,
      label: game.name,
      without: {
        ...filters,
        gameCategoryIds: filters.gameCategoryIds!.filter(
          (id) => id !== game.id,
        ),
      },
    });
  for (const tag of interpretation.moodTags)
    chips.push({
      key: `mood:${tag}`,
      label: tag,
      without: {
        ...filters,
        moodTags: filters.moodTags!.filter((other) => other !== tag),
      },
    });
  for (const person of interpretation.people)
    chips.push({
      key: `person:${person}`,
      label: clipSearchCopy.chips.person(person),
      without: {
        ...filters,
        people: filters.people!.filter((other) => other !== person),
      },
    });
  if (interpretation.phrase)
    chips.push({
      key: "phrase",
      label: clipSearchCopy.chips.phrase(interpretation.phrase),
      without: { ...filters, phrase: null },
    });
  return chips;
}

/** Whether filters still narrow or rank anything; with none left, a search would only list every clip. */
export function hasAnyFilter(filters: ClipSearchFilters): boolean {
  return !!(
    filters.startDate ||
    filters.endDate ||
    filters.gameCategoryIds?.length ||
    filters.moodTags?.length ||
    filters.people?.length ||
    filters.phrase
  );
}

/** The searcher's time zone, so "yesterday" and "last month" are their days; the API falls back to UTC. */
export function browserTimeZone(): string | null {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || null;
  } catch {
    return null;
  }
}

const storageKey = "reelshelf.clipSearch";

/** Keeps the search for this tab, so going back from a clip it found brings the results back. */
export function saveClipSearch(state: ClipSearchState | null) {
  try {
    if (state) sessionStorage.setItem(storageKey, JSON.stringify(state));
    else sessionStorage.removeItem(storageKey);
  } catch {
    // Without storage the search simply doesn't outlive the page.
  }
}

export function loadClipSearch(): ClipSearchState | null {
  try {
    const stored = sessionStorage.getItem(storageKey);
    if (!stored) return null;
    const parsed = JSON.parse(stored) as {
      query?: unknown;
      filters?: Record<string, unknown> | null;
    };
    if (typeof parsed.query !== "string") return null;
    const filters = parsed.filters;
    return {
      query: parsed.query,
      filters: filters
        ? {
            startDate: readDate(filters.startDate),
            endDate: readDate(filters.endDate),
            gameCategoryIds: readStrings(filters.gameCategoryIds),
            moodTags: readStrings(filters.moodTags),
            people: readStrings(filters.people),
            phrase: typeof filters.phrase === "string" ? filters.phrase : null,
          }
        : null,
    };
  } catch {
    return null;
  }
}

const readDate = (value: unknown) =>
  typeof value === "string" && !Number.isNaN(Date.parse(value))
    ? new Date(value)
    : null;
const readStrings = (value: unknown) =>
  Array.isArray(value)
    ? value.filter((item): item is string => typeof item === "string")
    : [];
