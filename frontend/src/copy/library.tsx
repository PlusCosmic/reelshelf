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
  loading: "Loading your library…",
  loadFailed: "Your library didn't load.",
  gameNotFound: "That game isn't on your shelf.",
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
    eyebrow: (stats: ReactNode) => <>Your library · {stats}</>,
    title: (unviewed: number) =>
      unviewed === 0 ? (
        <>
          Welcome back. <em>Nothing new</em> on the shelf.
        </>
      ) : unviewed === 1 ? (
        <>
          Welcome back. <em>One new clip</em> is waiting.
        </>
      ) : (
        <>
          Welcome back. <em>{unviewed} new clips</em> are waiting.
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
      meta: (count: number) => `${twitchClips(count)} · not on your shelf yet`,
      add: "Add to shelf",
      dismiss: "Not interested",
    },
  },
  banner: {
    eyebrow: (isCustom: boolean) => (isCustom ? "Category" : "Game"),
    empty: "No clips in this book yet",
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
  /** The totals beside the library's greeting and a book's banner. */
  statLine: (clips: number, duration: string) =>
    `${clips.toLocaleString()}\u00a0${clips === 1 ? "clip" : "clips"} · ${duration}`,
  list: {
    loading: "Loading clips…",
    loadFailed: "Your clips didn't load.",
    empty: "No clips match.",
  },
  clip: {
    open: (title: string) => `Open ${title}`,
    uncategorised: "Uncategorised",
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
      `Permanently delete "${title}"? This can't be undone.`,
    deleteFailed: "That clip wasn't deleted. Try again.",
  },
  shareDialog: {
    eyebrow: "Share clip",
    close: "Close",
    creating: "Making a link…",
    stillProcessing:
      "This clip is still processing. You can share it once it's ready.",
    failed: "The link wasn't made. Try again.",
    retry: "Retry",
    link: "Share link",
    copied: "Copied",
    copy: "Copy",
    note: "Anyone with this link can watch this clip.",
  },
  shared: "Shared",
  backToLibrary: "Library",
} as const;
