/** The clip player and what stands in for it while a clip's video isn't ready. */
export const videoCopy = {
  player: {
    unsupported: "This clip can't play on this device.",
    fatal: "This clip didn't play. Try reloading the page.",
  },
  processing: {
    encodeFailed: {
      title: "This clip couldn't be processed.",
      detail:
        "Something went wrong getting it ready to play. Try uploading it again.",
    },
    uploadFailed: {
      title: "This clip's upload didn't finish.",
      detail: "Try uploading it again.",
    },
    title: (progress: number | null) =>
      progress === null ? "Processing" : `Processing ${progress}%`,
    detail: "It'll play here as soon as it's ready.",
  },
} as const;
