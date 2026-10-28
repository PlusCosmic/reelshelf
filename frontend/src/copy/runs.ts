import { commonCopy } from "./common";

/**
 * Words shared by the internal review tools for model runs (legend detection, transcription and summaries): run
 * tables, run details, re-run forms and the units their numbers are shown in.
 */
export const runsCopy = {
  columns: {
    model: "Model",
    reasoning: "Reasoning",
    prompt: "Prompt",
    succeeded: "Succeeded",
    failed: "Failed",
    queued: "Queued",
    inputTokens: "Input tokens",
    cached: "Cached",
    outputTokens: "Output tokens",
    avgTime: "Avg time",
    perThousandClips: "Per 1k clips",
  },
  filters: {
    all: "All",
    failed: "Failed",
    notRun: "Not run",
  },
  /** A run's reasoning effort when it used the model's default. */
  defaultEffort: "default",
  reasoningEffort: {
    label: "Reasoning effort",
    default: "Default reasoning",
    option: (effort: string) => `${effort} reasoning`,
  },
  provider: {
    label: "Provider",
    default: "Default provider",
  },
  rerun: {
    modelPlaceholder: "Model (default if empty)",
    modelLabel: "Model to run",
    queueing: "Queueing…",
    runAgain: "Run again",
  },
  backfillAll: "Backfill all clips",
  queuedClips: (count: number) => `Queued ${commonCopy.clips(count)}.`,
  moveHint: "j / k to move",
  loadingClips: "Loading clips…",
  clipsNotLoaded: "Clips could not be loaded.",
  noClipsMatch: "No clips match this filter.",
  openClip: "Open clip",
  notRunYet: "Not run yet.",
  runsHeading: "Runs",
  rawResponse: "Raw response",
  retryingAfter: (error: string) => `Retrying after: ${error}`,
  waitingForWorker: "Waiting for a worker…",
  took: (ms: number) => ` · took ${commonCopy.seconds(ms / 1000)}`,
  units: {
    percent: (value: number | string) => `${value}%`,
    usd: (amount: number | string) => `$${amount}`,
    lessThanCent: "<$0.01",
    thousandTokens: (thousands: string) => `${thousands}k`,
    seconds: (seconds: number) => `${seconds}s`,
    minutes: (minutes: number) => `${minutes}m`,
    hoursMinutes: (hours: number, minutes: number) => `${hours}h ${minutes}m`,
    durationMs: (ms: number) => commonCopy.seconds(ms / 1000),
  },
} as const;
