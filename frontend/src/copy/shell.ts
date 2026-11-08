/** The signed-in app's chrome: the topbar, its navigation and the profile menu. */
export const shellCopy = {
  /** The session check failed for a reason other than being signed out, such as the network. */
  sessionUnavailable: "Reelshelf couldn't check whether you're signed in.",
  brandLink: "Reelshelf, your library",
  nav: {
    library: "Library",
    collections: "Collections",
  },
  profileMenu: {
    settings: "Settings",
    legendDetection: "Legend detection",
    transcription: "Transcription",
    accounts: "Accounts",
    reports: "Reports",
    signOut: "Sign out",
  },
} as const;
