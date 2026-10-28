import { commonCopy } from "./common";

/** A clip's own page: the player, its details, the rest of the session and more from its game. */
export const clipCopy = {
  loading: "Loading player…",
  notFound: "This clip could not be found.",
  strip: {
    label: "Where this clip lives",
    backTo: "Back to",
    theShelf: "the shelf",
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
    size: "Size",
    watched: "Watched",
    watchedYes: "Yes",
    watchedNo: "Not yet",
    sharing: "Sharing",
    sharedWithLink: "Anyone with the link",
    onlyYou: "Only you",
  },
  stage: "Player",
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
