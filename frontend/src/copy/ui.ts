/** Generic controls shared across the app: avatars, search, selects and paged lists. */
export const uiCopy = {
  avatar: {
    defaultName: "You",
    defaultInitial: "Y",
    alt: "Profile picture",
  },
  searchPlaceholder: "Search clips, games, tags…",
  selectPlaceholder: "Choose…",
  loadMore: {
    /** `noun` is the plural noun for the items, as in "Load more clips". */
    failed: (noun: string) => `More ${noun} didn't load.`,
    button: (noun: string) => `Load more ${noun}`,
    loading: (noun: string) => `Loading more ${noun}…`,
  },
} as const;
