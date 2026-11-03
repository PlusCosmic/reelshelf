import { commonCopy } from "./common";
import { plural } from "./plural";

/**
 * The bin: where deleted clips wait before they're permanently deleted, the undo note that follows a delete, selecting clips in
 * the library list, and sorting through a list one clip at a time.
 */
export const binCopy = {
  name: "Bin",
  toast: {
    binned: (count: number) =>
      count === 1
        ? "Clip moved to the bin."
        : `${commonCopy.clips(count)} moved to the bin.`,
    restored: (count: number) =>
      count === 1 ? "Clip put back." : `${commonCopy.clips(count)} put back.`,
    undo: "Undo",
    undoing: "Putting back…",
    undoHint: "Ctrl+Z",
    openBin: "Open the bin",
    dismiss: "Dismiss",
    binFailed: "Nothing was moved to the bin. Try again.",
    undoFailed: "Those clips weren't put back. Put them back from the bin.",
  },
  libraryLink: (count: number) => `Bin · ${count.toLocaleString()}`,
  libraryLinkLabel: (count: number) =>
    `The bin, ${commonCopy.clips(count)} waiting to be deleted`,
  page: {
    eyebrow: "Your library",
    title: "Bin",
    intro: (days: number) =>
      `Deleted clips wait here for ${plural(days, "day", "days")}, then they're permanently deleted. They still count towards your plan until then.`,
    empty: "The bin is empty.",
    loading: "Loading the bin…",
    loadFailed: "The bin didn't load.",
    backToLibrary: "Back to your library",
    count: (count: number) => commonCopy.clips(count),
    restore: "Put back",
    restoring: "Putting back…",
    deleteNow: "Delete now…",
    deletesIn: (days: number) =>
      days <= 0
        ? "Deleted within the hour"
        : days === 1
          ? "Deleted within a day"
          : `Deleted in ${days} days`,
    binnedOn: (when: string) => `Binned ${when}`,
    restoreSelected: (count: number) => `Put back ${count.toLocaleString()}`,
    deleteSelected: (count: number) => `Delete ${count.toLocaleString()} now…`,
    emptyBin: "Empty the bin…",
    restoreFailed: "Those clips weren't put back. Try again.",
    deleteFailed:
      "Some clips weren't deleted. They're still in the bin; try again.",
    held: (count: number) =>
      `${commonCopy.clips(count)} can't be deleted right now and will stay in the bin.`,
    confirm: {
      title: (count: number | "all") =>
        count === "all"
          ? "Empty the bin?"
          : `Permanently delete ${commonCopy.clips(count)}?`,
      body: (count: number) =>
        count === 1
          ? "This clip and its video will be permanently deleted. This can't be undone."
          : `These ${commonCopy.clips(count)} and their videos will be permanently deleted. This can't be undone.`,
      confirm: "Permanently delete",
      deleting: "Deleting…",
    },
    select: (title: string) => `Select ${title}`,
  },
  /** Selecting clips in the library list. */
  selection: {
    start: "Select clips",
    toggle: (title: string) => `Select ${title}`,
    month: (month: string) => `Select every clip from ${month}`,
    selected: (count: number) => `${count.toLocaleString()} selected`,
    allMatching: (count: number) => `All ${commonCopy.clips(count)} that match`,
    selectAll: (count: number) => `Select all ${count.toLocaleString()}`,
    bin: "Move to bin",
    binning: "Moving…",
    clear: "Clear",
    barLabel: "Selected clips",
    hint: "Shift-click selects a run · X selects · J and K move · Delete bins",
  },
  /** Sorting through a list: keep or bin each clip in turn. */
  sort: {
    start: "Sort through",
    startLabel: (count: number) =>
      `Sort through these ${commonCopy.clips(count)}, keeping or binning each in turn`,
    label: "Sorting through",
    keep: "Keep",
    keepKey: "K",
    bin: "Bin",
    binKey: "X",
    stop: "Stop",
    tally: (kept: number, binned: number) =>
      `${kept.toLocaleString()} kept · ${binned.toLocaleString()} binned`,
    doneTitle: "That's the lot.",
    doneBody: (kept: number, binned: number) =>
      `You kept ${commonCopy.clips(kept)} and binned ${commonCopy.clips(binned)}.`,
    backToList: "Back to the list",
  },
} as const;
