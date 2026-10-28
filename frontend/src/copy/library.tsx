import type { ReactNode } from "react";
import { commonCopy } from "./common";
import { plural } from "./plural";

const twitchClips = (count: number) =>
  plural(count, "Twitch clip", "Twitch clips");

/** A book's name and counts, as a screen reader hears it on the shelf. */
const describeBook = (name: string, clips: number, unviewed: number) => {
  if (clips === 0) return `${name}, no clips yet`;
  const counted = commonCopy.clips(clips);
  return unviewed > 0
    ? `${name}, ${counted}, ${unviewed} new`
    : `${name}, ${counted}`;
};

/** The library: the bookcase of games, the bar that takes over from it, and the clip list below. */
export const libraryCopy = {
  loading: "Loading your archive…",
  loadFailed: "The archive could not be loaded.",
  gameNotFound: "This game could not be found.",
  newCount: (count: number) => `${count} new`,
  noClipsYet: "No clips yet",
  /** The plural noun the clip list's "Load more" names. */
  clipsNoun: "clips",
  dropHint: {
    intoBook: (name: string) => (
      <>
        Drop anywhere to add to <strong>{name}</strong>
      </>
    ),
    orAnotherBook: " · or onto another book",
    ontoShelf: "Drop on a book to add these clips",
    orNewBook: " · or on New book for another game",
  },
  dropOutsideBook: "Drop clips onto a book, or onto New book for another game.",
  /** The first file that couldn't be added, and how many more were skipped with it. */
  rejectedNotice: (message: string, more: number) =>
    more > 0 ? `${message} ${more} more skipped.` : message,
  chooseClips: "Choose clips to add",
  greeting: {
    eyebrow: (stats: ReactNode) => <>Your archive - {stats}</>,
    title: (unviewed: number) => (
      <>
        Welcome back. <em>{unviewed || "No"} new clips</em> are waiting on the
        shelf.
      </>
    ),
  },
  filters: {
    tags: "Tags",
    allTags: "All tags",
  },
  bookcase: {
    label: "Your shelf",
    spine: (
      name: string,
      clips: number,
      unviewed: number,
      twitchCount: number,
    ) =>
      twitchCount > 0
        ? `${describeBook(name, clips, unviewed)}, ${twitchClips(twitchCount)} to bring in`
        : describeBook(name, clips, unviewed),
    empty: "Empty",
    faceOut: (name: string, clips: number, unviewed: number) =>
      `${describeBook(name, clips, unviewed)}. Pulled out; press to put it back on the shelf`,
    ghost: (name: string, count: number) =>
      `${name}, not on your shelf, ${twitchClips(count)}`,
    addGame: "Add a game or category",
    newBook: "New book",
    addGameShort: "Add a game",
    scrollLeft: "Scroll the shelf left",
    scrollRight: "Scroll the shelf right",
    tipClips: (count: number) =>
      count === 0 ? "No clips yet" : commonCopy.clips(count),
    tipTwitch: (count: number) => `${twitchClips(count)} to bring in`,
    dropNewBook: "Drop to start a new book",
    dropOnGhost: (name: string | undefined) =>
      `Drop to add ${name ?? "this game"} to your shelf`,
    dropOnBook: (name: string | undefined) =>
      `Drop into ${name ?? "this book"}`,
    ghostCard: {
      meta: (count: number) =>
        `${plural(count, "of your Twitch clips", "of your Twitch clips")} · not on your shelf yet`,
      add: "Add to shelf",
      dismiss: "Not interested",
    },
  },
  banner: {
    eyebrow: (isCustom: boolean) =>
      isCustom ? "Custom category" : "Game category",
    empty: "No clips on this shelf yet",
    addClips: "Add clips",
    putBack: "Put it back on the shelf",
  },
  shelfBar: {
    allGames: "All games",
    label: "Current game",
    trigger: (name: string, newCount: number) =>
      `Showing ${name}${newCount > 0 ? `, ${newCount} new` : ""}. Change game`,
    chooseGame: "Choose a game",
    closeGames: "Close game list",
    optionClips: (count: number) =>
      count === 0 ? "No clips yet" : commonCopy.clips(count),
    searchLabel: "Search clips",
    searchPlaceholder: (game: string | undefined) =>
      game ? `Search ${game} clips` : "Search every clip",
    closeSearch: "Close search",
    backToShelf: "Back to shelf",
  },
  legendPicker: {
    trigger: (legend: string | null) =>
      legend ? `Played as ${legend}. Change legend` : "Filter by legend",
    anyLegend: "Any legend",
    clear: "Show every legend",
    playedAs: "Played as",
    count: (legends: number) =>
      `${plural(legends, "legend", "legends")}, most played first`,
  },
  /** The totals beside the archive and a book's banner. */
  statLine: (clips: number, duration: string, size: string) =>
    `${clips}\u00a0clips - ${duration} - ${size}`,
  list: {
    loading: "Loading clips…",
    loadFailed: "Clips could not be loaded.",
    empty: "No clips match this view.",
  },
  clip: {
    open: (title: string) => `Open ${title}`,
    uncategorized: "Uncategorized",
    new: "New",
    nowPlaying: "Now playing",
    notWatched: "Not watched yet",
    game: "Game",
    /** Read before the legend's name in the legend column. */
    playedAs: "Played as ",
    unknownDate: "Unknown",
  },
  actions: {
    share: "Share",
    addToCollection: "Add to collection",
    preparingDownload: "Preparing download…",
    download: "Download",
    confirmDelete: (title: string) =>
      `Delete "${title}"? This removes the video and frees its storage.`,
    deleteFailed: "The clip could not be deleted.",
  },
  shareDialog: {
    eyebrow: "Share clip",
    close: "Close share dialog",
    creating: "Creating share link…",
    stillProcessing: "This clip is still processing and cannot be shared yet.",
    failed: "The share link could not be created. Try again.",
    retry: "Retry",
    link: "Share link",
    copied: "Copied",
    copy: "Copy",
    note: "Anyone with this link can watch the shared clip.",
  },
  shared: "Shared",
  backToLibrary: "Library",
} as const;
