import type { ReactNode } from "react";

/** Bringing clips in from Twitch: the strip inside a book and the full list at /upload?source=twitch. */
export const twitchImportCopy = {
  /** Inside an open book: the owner's Twitch clips of this game not yet on the shelf. */
  strip: {
    label: "Twitch clips to bring in",
    bringingIn: (current: number, total: number) =>
      `Bringing in ${current} of ${total} Twitch clips…`,
    fromThisGame: (clips: number) =>
      clips === 1
        ? "1 of your Twitch clips is from this game"
        : `${clips} of your Twitch clips are from this game`,
    notOnShelf: "Not on your shelf yet",
    newest: (date: string) => ` · newest ${date}`,
    bringIn: (clips: number) =>
      clips === 1 ? "Bring it in" : `Bring in all ${clips}`,
    choose: "Choose…",
    notNow: "Not now",
  },
  panel: {
    label: "Twitch clips",
    checking: "Checking your Twitch account…",
    loadFailed: "Your Twitch clips could not be loaded.",
    tryAgain: "Try again",
    notLinked: {
      title: "Link Twitch to bring your clips over.",
      copy: "Pick clips from your channel and they copy straight into your library.",
      action: "Link Twitch",
    },
    needsAuthorization: {
      title: "Reconnect Twitch to bring clips over.",
      copy: (login: string | null | undefined) =>
        `Twitch${login ? ` (@${login})` : ""} is linked, but Reelshelf needs one more permission to copy your clips. Reconnecting changes nothing else.`,
      action: "Reconnect Twitch",
    },
    manageAccounts: (settings: ReactNode) => (
      <>Manage linked accounts in {settings}.</>
    ),
    settingsLink: "Settings",
  },
  list: {
    filtersLabel: "Twitch clip filters",
    account: (login: string | null | undefined) =>
      login ? `@${login}` : "Twitch",
    clipsFrom: "Clips from",
    timeWindow: "Time window",
    windows: {
      week: "Last 7 days",
      month: "Last 30 days",
      quarter: "Last 90 days",
      all: "All time",
    },
    refresh: "Refresh",
    gamesToAdd:
      "Some of these clips are from games that aren't on your shelf yet.",
    addingGame: (game: string) => `Adding ${game}…`,
    addGame: (game: string, clips: number) => `Add ${game} (${clips})`,
    actionsLabel: "Twitch import actions",
    selected: (selected: number, total: number) =>
      `${selected} of ${total} selected`,
    needGame: (count: number) => `${count} need a game`,
    importing: (count: number) => `${count} importing`,
    saved: (count: number) => `${count} saved`,
    addToLibrary: (count: number) => `Add ${count} to library`,
    empty: "No clips from your channel in that time. Try a longer window.",
    loadMoreLabel: "clips",
  },
  card: {
    select: (title: string) => `Select ${title}`,
    titleFor: (title: string) => `Title for ${title}`,
    views: (views: string) => `${views} views`,
    twitchLink: "Twitch",
    game: "Game",
    gameFor: (title: string) => `Game for ${title}`,
    noGame: "No game",
    openCollection: "Open collection",
    retry: "Retry",
  },
  status: {
    already_imported: "in library",
    needs_game: "needs game",
    ready: "ready",
    importing: "importing",
    imported: "imported",
    filing: "filing",
    saved: "saved",
    duplicate: "in library",
    error: "failed",
  },
  chooseGame: "Choose game",
  /** The game a clip Twitch tagged was from, when Twitch didn't name it. */
  thisGame: "this game",
  filingFailed: "Added to your library, but it didn't go into its session.",
  leaveWhileImporting:
    "Leave while Twitch clips are still importing? Clips already copied stay in your library.",
  addGameFailed: "Couldn't add that game. Try again.",
  alreadyInLibrary: "This Twitch clip is already in your library.",
} as const;
