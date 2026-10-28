/** The add-a-book modal: searching IGDB for a game or making a custom category. */
export const addGameCopy = {
  eyebrow: "New book",
  title: "Add a game",
  /** The title when clips dropped on New book are waiting for their game. */
  pendingTitle: (clips: number) =>
    `Which game ${clips === 1 ? "is this clip" : `are these ${clips} clips`} from?`,
  closeLabel: "Close",
  addFailed: "Couldn't add that. Try again.",
  tabs: {
    label: "Category type",
    search: "Game",
    custom: "Something else",
  },
  search: {
    label: "Game title",
    placeholder: "Search for a game…",
    searching: "Searching",
    noResults:
      "No games match that. Try another name, or add it under Something else.",
    hint: "Find a game to give it a book on your shelf.",
  },
  custom: {
    nameLabel: "Name",
    namePlaceholder: "Snowboarding, hiking, speedruns…",
    coverLabel: "Cover image URL",
    coverPlaceholder: "https://…",
    adding: "Adding…",
    submit: "Add book",
  },
} as const;
