import { commonCopy } from "./common";

/** Adding clips from the computer: the drop queue, the corner tray and why an upload failed. */
export const uploadCopy = {
  /** Files the drop queue turns away before uploading. */
  rejected: {
    unsupportedType: "Choose an MP4, MOV, WebM, or MKV video file.",
    tooLarge: "This file is larger than the 4 GB upload limit.",
  },
  /** A clip longer than the plan keeps, with both lengths already formatted. */
  tooLong: (length: string, max: string) =>
    `This clip is ${length} long, and your plan keeps clips of up to ${max}. Trim it, or upgrade for longer clips.`,
  duplicateInBook: "Already added to this book.",
  errors: {
    alreadyUploaded: "This video has already been uploaded.",
    planLimit: "Your plan doesn't allow more clips.",
    failed: "The upload failed.",
    notPrepared: "The upload could not be prepared.",
    unsupportedProtocol: (protocol: string) =>
      `The upload could not start: unsupported upload protocol "${protocol}".`,
    partRefused: (part: number, status: number) =>
      `Part ${part} was refused (${status}).`,
    noEtag: (part: number) => `Storage sent no ETag for part ${part}.`,
    noPartUrl: (part: number) => `No URL came back for part ${part}.`,
  },
  /** Where a clip is on its way to the shelf. */
  stage: {
    paused: "Paused",
    uploading: (percent: number) => `Uploading ${percent}%`,
    processing: "Processing",
    processingPercent: (percent: number) => `Processing ${percent}%`,
    failed: "Couldn’t be processed",
  },
  unknownGame: "Unknown game",
  tray: {
    label: "Clips being added",
    lineFailed: "Couldn’t upload",
    waiting: "Waiting",
    onTheShelf: "On the shelf",
    aBook: "a book",
    addingToBook: (clips: number, book: string) =>
      `Adding ${clips} clips to ${book}`,
    addingToBooks: (clips: number, books: number) =>
      `Adding ${clips} clips to ${books} books`,
    paused: "Paused",
    stillUploading: (count: number) => `${count} still uploading`,
    processing: (clips: number) => `Processing ${commonCopy.clips(clips)}`,
    safeToClose: "Safe to close this tab",
    added: (clips: number) => `${commonCopy.clips(clips)} added`,
    failed: (count: number) => `${count} didn’t make it`,
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
