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
      title: "Link Twitch to import your clips.",
      copy: "Once your Twitch account is linked, Reelshelf can list the clips from your channel and copy the ones you pick straight into your library. No downloading and re-uploading.",
      action: "Link Twitch",
    },
    needsAuthorization: {
      title: "Reconnect Twitch to allow clip downloads.",
      copy: (login: string | null | undefined) =>
        `Your Twitch account${login ? ` (@${login})` : ""} is linked, but Reelshelf needs permission to fetch your clips. Reconnecting takes you through Twitch once more and changes nothing else on your account.`,
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
    gamesToAdd: "Twitch tagged some clips with games not in your library yet.",
    addingGame: (game: string) => `Adding ${game}…`,
    addGame: (game: string, clips: number) => `Add ${game} (${clips})`,
    actionsLabel: "Twitch import actions",
    selected: (selected: number, total: number) =>
      `${selected} of ${total} selected`,
    needGame: (count: number) => `${count} need game`,
    importing: (count: number) => `${count} importing`,
    saved: (count: number) => `${count} saved`,
    addToLibrary: (count: number) => `Add ${count} to library`,
    empty:
      "No clips found on your channel for this period. Try a longer window.",
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
    error: "error",
  },
  chooseGame: "Choose game",
  /** The game a clip Twitch tagged was from, when Twitch didn't name it. */
  thisGame: "this game",
  filingFailed: (detail: string) =>
    `Added to your library, but the session collection could not be updated (${detail}).`,
  leaveWhileImporting:
    "Leave while Twitch clips are still importing? Clips already copied stay in your library.",
  addGameFailed: "The game could not be added.",
  alreadyInLibrary: "This Twitch clip is already in your library.",
} as const;
