/** The Discord Activity's watch room, in the order it reads. */
export const activityCopy = {
  kicker: "Watch room",
  joining: "Joining the room…",
  failure: {
    notInInstance:
      "Couldn't confirm you're in this Activity. Rejoin it from the voice channel.",
    switchedOff: "The watch room isn't switched on yet.",
    couldNotJoin: "Couldn't join the room.",
  },
  room: {
    connecting: "Connecting to the room…",
    reconnecting: "Reconnecting to the room…",
    somethingWentWrong: "Something went wrong.",
    present: "In the room",
    host: "Host",
    guest: "Guest",
  },
  role: {
    hosting: (accountName: string | null) =>
      `You're hosting. Add clips from ${accountName}'s shelf below and everyone watches them with you.`,
    hostIs: (hostName: string) => `${hostName} is hosting.`,
    noHost: "Nobody with a Reelshelf shelf is here yet.",
    guest: (hosting: string) =>
      `${hosting} You're watching as a guest. Have a Reelshelf account? Link Discord in Settings to share your clips.`,
    locked: (hosting: string) => `${hosting} They've locked the queue for now.`,
    member: (hosting: string) =>
      `${hosting} Add clips from your shelf to the queue below.`,
  },
  nowPlaying: {
    fullView: "Full view",
    exitFullView: "Exit full view",
    exitFullViewHint: "Exit full view (Esc)",
    fromShelf: (ownerName: string) => `from ${ownerName}'s shelf`,
    skip: "Skip",
    stop: "Stop",
  },
  player: {
    turnSoundOn: "Turn sound on",
    cantPlay: "This clip couldn't be played here.",
    mute: "Mute",
    unmute: "Unmute",
    volume: "Volume",
  },
  reactions: {
    label: "React",
    react: (emoji: string) => `React ${emoji}`,
  },
  queue: {
    upNext: "Up next",
    locked: " · locked",
    lock: "Lock queue",
    unlock: "Unlock queue",
    empty: "Nothing queued yet.",
    ownerShelf: (ownerName: string) => `${ownerName}'s shelf`,
    playNow: (title: string) => `Play ${title} now`,
    moveUp: (title: string) => `Move ${title} up`,
    moveDown: (title: string) => `Move ${title} down`,
    remove: (title: string) => `Remove ${title} from the queue`,
  },
  picker: {
    title: "Add from your shelf",
    search: "Search your clips",
    failed: "Couldn't load your clips.",
    noMatch: "No clips match that search.",
    empty: "Your shelf is empty.",
    add: (title: string) => `Add ${title} to the queue`,
    inQueue: "In the queue",
    encoding: "Still encoding",
    loading: "Loading clips",
    loadMore: "Load more",
  },
} as const;
