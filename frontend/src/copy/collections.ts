import { commonCopy } from "./common";

/** Collections: the ledger of them, one collection's page, and the dialogs for making and sharing them. */
export const collectionsCopy = {
  list: {
    loading: "Loading collections…",
    loadFailed: "Collections could not be loaded.",
    eyebrow: (count: number) => `Collections · ${count}`,
    title: "Collections",
    search: "Search collections",
    newCollection: "New collection",
    empty: "No collections yet.",
    noMatch: (query: string) => `No collections match “${query}”.`,
    groups: {
      mine: { title: "Made by you" },
      shared: {
        title: "Shared with you",
        note: "Collections friends invited you to",
      },
      sessions: {
        title: "Sessions",
        note: "Made for you from each night of play",
      },
    },
    row: {
      empty: "Empty",
      noClips: "No clips yet",
      updated: "Updated",
    },
    create: {
      submit: "Make collection",
      saving: "Making…",
    },
  },
  minutes: (minutes: number) => `${minutes} min`,
  private: "Private",
  collection: {
    loading: "Loading collection…",
    loadFailed: "This collection could not be loaded.",
    back: "Collections",
    eyebrow: (
      stats: { clips: number; minutes: number } | null,
      updated: string,
    ) =>
      `${stats ? `Collection · ${commonCopy.clips(stats.clips)} · ${stats.minutes} min` : "Collection"} · Updated ${updated}`,
    people: (sharing: string) => `People: ${sharing}`,
    invite: "Invite",
    edit: "Edit",
    playInOrder: "Play in order",
    gameShare: (game: string, clips: number) =>
      `${game}: ${commonCopy.clips(clips)}`,
    empty:
      "No clips in this collection yet. Add them from a clip's page with Add to collection.",
    changeFailed: "That change could not be saved. Try again.",
    editTitle: "Edit collection",
    confirmDelete: (name: string) =>
      `Delete "${name}"? The clips stay in your library.`,
  },
  form: {
    eyebrow: "Collection",
    name: "Name",
    namePlaceholder: "Ranked highlights, Friday night…",
    description: "Description (optional)",
    descriptionPlaceholder: "What goes in it",
    saveFailed: "The collection could not be saved.",
    delete: "Delete collection",
  },
  rows: {
    reorderHelp: "Drag, or press the up and down arrow keys, to move a clip.",
    untitled: "Clip",
    moved: (title: string, position: number, total: number) =>
      `${title} moved to ${position} of ${total}`,
    move: (title: string) => `Move ${title}`,
    notWatchedYet: "Not watched yet",
    you: "you",
    addedBy: (name: string) => `Added by ${name}`,
    unknownGame: "Game",
    takeOutLabel: (title: string) => `Take ${title} out of this collection`,
    takeOut: "Take out of this collection",
  },
  addTo: {
    eyebrow: "Add to collection",
    loading: "Loading your collections…",
    none: "You have no collections yet. Make one below and this clip goes in first.",
    yours: "Your collections",
    newName: "New collection name",
    making: "Making…",
    makeAndAdd: "Make and add",
    newCollection: "New collection…",
  },
  people: {
    title: "People",
    onCollection: "On this collection",
    you: "You",
    owner: "Made it",
    member: "Can add clips",
    remove: "Remove",
    leave: "Leave",
    confirmLeave: (name: string) =>
      `Leave "${name}"? You'll need inviting back.`,
    invite: "Invite",
    usernamePlaceholder: "Their Discord or Twitch username",
    usernameLabel: "Username to invite",
    inviting: "Inviting…",
    note: "People you invite can watch everything in it and add their own clips.",
  },
  changeFailed: "That change could not be saved.",
  /** How the viewer's side of a collection's sharing reads. */
  sharing: {
    unknownGame: "Unknown game",
    sharedBy: (owner: string | undefined) => `Shared by ${owner ?? "a friend"}`,
    sharedWith: (names: string) => `Shared with ${names}`,
  },
} as const;
