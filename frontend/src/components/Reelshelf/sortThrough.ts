/**
 * The running tally while sorting through a list, kept for this tab only so it survives moving from clip to clip
 * (and a reload) but starts again with the next sort.
 */
export type SortTally = { kept: number; binned: number };

const KEY = "reelshelf.sort-through";

export function readSortTally(): SortTally {
  try {
    const stored = JSON.parse(window.sessionStorage.getItem(KEY) ?? "null");
    if (
      stored &&
      typeof stored.kept === "number" &&
      typeof stored.binned === "number"
    )
      return { kept: stored.kept, binned: stored.binned };
  } catch {
    // Unreadable or unavailable storage starts from nothing.
  }
  return { kept: 0, binned: 0 };
}

export function writeSortTally(tally: SortTally) {
  try {
    window.sessionStorage.setItem(KEY, JSON.stringify(tally));
  } catch {
    // Unremembered is fine; the tally is only a count.
  }
}

export function startSortTally() {
  writeSortTally({ kept: 0, binned: 0 });
}

export function recordSort(kind: keyof SortTally): SortTally {
  const tally = readSortTally();
  const next = { ...tally, [kind]: tally[kind] + 1 };
  writeSortTally(next);
  return next;
}

/** Whether a key press is for the page's shortcuts, not text being typed or a shortcut of the browser's own. */
export function isShortcutKey(event: KeyboardEvent) {
  const target = event.target;
  if (event.ctrlKey || event.metaKey || event.altKey) return false;
  if (
    target instanceof HTMLElement &&
    (target.isContentEditable ||
      ["INPUT", "TEXTAREA", "SELECT"].includes(target.tagName))
  )
    return false;
  // A dialog over the page has the keys.
  return !document.querySelector('[aria-modal="true"]');
}
