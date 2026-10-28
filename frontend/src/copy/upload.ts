import { commonCopy } from "./common";

/** Adding clips from the computer: the drop queue, the corner tray and why an upload failed. */
export const uploadCopy = {
  /** Files the drop queue turns away before uploading. */
  rejected: {
    unsupportedType: "Not a video we can take. Use MP4, MOV, WebM or MKV.",
    tooLarge: "Too big to upload. Each file can be up to 4 GB.",
  },
  /**
   * A clip longer than the plan keeps, with the lengths already formatted. `longer` is what the best plan that can be
   * bought keeps, when that is longer than the plan's own; otherwise there's no upgrade to offer.
   */
  tooLong: (length: string, max: string, longer: string | null) =>
    longer
      ? `This clip is ${length}. Your plan keeps clips up to ${max}, so trim it or upgrade for clips up to ${longer}.`
      : `This clip is ${length}, and clips can be up to ${max}. Trim it, then add it again.`,
  duplicateInBook: "Already in this book.",
  errors: {
    alreadyUploaded: "You've already added this clip.",
    planLimit: "Your library is full. Delete some clips to add more.",
    failed: "The upload didn't finish.",
    notStarted: "The upload couldn't start. Try again.",
    partFailed: "Part of the upload didn't go through. Try again.",
  },
  /** Where a clip is on its way to the shelf. */
  stage: {
    paused: "Paused",
    uploading: (percent: number) => `Uploading ${percent}%`,
    processing: "Processing",
    processingPercent: (percent: number) => `Processing ${percent}%`,
    failed: "Couldn't be processed",
  },
  unknownGame: "Unknown game",
  tray: {
    label: "Clips being added",
    lineFailed: "Couldn't upload",
    waiting: "Waiting",
    onTheShelf: "On the shelf",
    aBook: "a book",
    addingToBook: (clips: number, book: string) =>
      `Adding ${commonCopy.clips(clips)} to ${book}`,
    addingToBooks: (clips: number, books: number) =>
      `Adding ${commonCopy.clips(clips)} to ${books} books`,
    paused: "Paused",
    stillUploading: (count: number) => `${count} still uploading`,
    processing: (clips: number) => `Processing ${commonCopy.clips(clips)}`,
    safeToClose: "Safe to close this tab",
    added: (clips: number) => `${commonCopy.clips(clips)} added`,
    failed: (count: number) => `${count} didn't upload`,
    allDone: "All done",
    resume: "Resume",
    pause: "Pause",
    collapse: "Collapse",
    keepOpen:
      "Keep this tab open until uploads finish. Processing carries on without it.",
    uploadsDone: "Uploads are done; you can close this tab.",
    clear: "Clear",
    dismiss: "Dismiss",
    book: {
      unknown: "A book",
      onTheShelf: (done: number, total: number) =>
        `${done} of ${total} on the shelf`,
      retry: "Retry",
      showFewer: "Show fewer",
      more: (count: number) => `${count} more`,
    },
  },
} as const;
