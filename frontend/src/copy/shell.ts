/** The signed-in app's chrome: the topbar, its navigation and the profile menu. */
export const shellCopy = {
  brandLink: "Reelshelf, your library",
  nav: {
    label: "Primary",
    library: "Library",
    collections: "Collections",
  },
  profileMenu: {
    open: "Account menu",
    settings: "Settings",
    legendDetection: "Legend detection",
    transcription: "Transcription",
    accounts: "Accounts",
    reports: "Reports",
    theme: (current: "light" | "dark") =>
      current === "dark" ? "Light mode" : "Dark mode",
    signOut: "Sign out",
  },
} as const;
