import { commonCopy } from "./common";

/** A clip's own page: the player, its details, the rest of the session and more from its game. */
export const clipCopy = {
  loading: "Loading clip…",
  notFound: "That clip isn't here. It may have been deleted.",
  strip: {
    label: "Where this clip lives",
    backTo: "Back to",
    theShelf: "the shelf",
    /** The back link when the clip was opened from the whole library rather than its own book. */
    theLibrary: "your library",
    position: (index: number, total: number) => `Clip ${index} of ${total}`,
    newCount: (count: number) => `${count} new`,
    theatre: "Theatre mode",
  },
  neighbour: {
    previous: "Previous",
    next: "Next",
    older: "Older",
    newer: "Newer",
    label: (direction: string, title: string) => `${direction} clip: ${title}`,
  },
  session: {
    title: "Same session",
    summary: (day: string, clips: number, from: string, to: string) =>
      `${day} · ${commonCopy.clips(clips)}, ${from} to ${to}`,
    nowPlaying: "Now playing",
    watched: "Watched",
    new: "New",
  },
  facts: {
    label: "Clip details",
    game: "Game",
    legend: "Legend",
    recorded: "Recorded",
    length: "Length",
    watched: "Watched",
    watchedYes: "Yes",
    watchedNo: "Not yet",
    sharing: "Sharing",
    sharedWithLink: "Anyone with the link",
    onlyYou: "Only you",
  },
  stage: "Player",
  binned: {
    title: "This clip is in the bin.",
    body: (days: number) =>
      days <= 0
        ? "It'll be permanently deleted within the hour unless you put it back."
        : days === 1
          ? "It'll be permanently deleted within a day unless you put it back."
          : `It'll be permanently deleted in ${days} days unless you put it back.`,
    restore: "Put back",
    restoring: "Putting back…",
  },
  playedAs: "Played as ",
  notWatchedYet: "Not watched yet",
  upNextIn: (collection: string) => `Up next in ${collection}`,
  moreFrom: (game: string) => `More from ${game}`,
  /** The link to the game's whole book, with its clip count when the shelf knows it. */
  allGameClips: (count: number | undefined, game: string) =>
    `All ${count !== undefined ? `${count} ` : ""}${game} clips`,
  allClips: (count: number | undefined) =>
    `All ${count !== undefined ? `${count} ` : ""}clips`,
} as const;
