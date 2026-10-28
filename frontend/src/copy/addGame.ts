/** The add-a-book modal: searching IGDB for a game or making a custom category. */
export const addGameCopy = {
  eyebrow: "Bookshelf category",
  title: "Add a new book",
  /** The title when clips dropped on New book are waiting for their game. */
  pendingTitle: (clips: number) =>
    `Which game ${clips === 1 ? "is this clip" : `are these ${clips} clips`} from?`,
  closeLabel: "Close modal",
  addFailed: "Could not add category.",
  tabs: {
    label: "Category type",
    search: "Search games",
    custom: "Custom category",
  },
  search: {
    label: "Game title",
    placeholder: "Search for a game…",
    searching: "Searching",
    noResults:
      "No games found. Try another search or create a custom category.",
    hint: "Search IGDB to add a game-backed book to your shelf.",
  },
  custom: {
    nameLabel: "Category name",
    namePlaceholder: "Snowboarding, hiking, speedruns…",
    coverLabel: "Cover image URL",
    coverPlaceholder: "https://…",
    adding: "Adding…",
    submit: "Add category",
  },
} as const;
