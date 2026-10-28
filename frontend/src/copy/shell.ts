/** The signed-in app's chrome: the topbar, its navigation and the profile menu. */
export const shellCopy = {
  brandLink: "Reelshelf library",
  nav: {
    label: "Primary",
    library: "Library",
    collections: "Collections",
  },
  profileMenu: {
    open: "Open profile menu",
    settings: "Settings",
    legendDetection: "Legend detection",
    transcription: "Transcription",
    accounts: "Accounts",
    reports: "Reports",
    theme: (current: "light" | "dark") =>
      current === "dark" ? "Light mode" : "Dark mode",
    logOut: "Log out",
  },
} as const;
