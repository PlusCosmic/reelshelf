import type {
  ClipTranscriptionReviewClip,
  ClipTranscriptionRun,
} from "@/shared/services/clipTranscription";

export const transcriptionFilters = [
  { value: "all", label: "All" },
  { value: "transcribed", label: "Transcribed" },
  { value: "silent", label: "No speech" },
  { value: "failed", label: "Failed" },
  { value: "not-run", label: "Not run" },
] as const;

export type TranscriptionFilter =
  (typeof transcriptionFilters)[number]["value"];

/**
 * What a run found: words, a clip with audio but nothing said, a clip with no audio track, or not finished yet.
 */
export function transcriptOutcome(run: ClipTranscriptionRun | null) {
  if (run === null) return "not-run";
  if (run.status === "failed") return "failed";
  if (run.status !== "succeeded") return "queued";
  if (run.hasAudio === false) return "no-audio";
  return run.transcript ? "transcribed" : "silent";
}

export function matchesTranscriptionFilter(
  clip: ClipTranscriptionReviewClip,
  filter: TranscriptionFilter,
) {
  const outcome = transcriptOutcome(clip.latestRun);
  switch (filter) {
    case "all":
      return true;
    case "silent":
      return outcome === "silent" || outcome === "no-audio";
    default:
      return outcome === filter;
  }
}

export function countByTranscriptionFilter(
  clips: ClipTranscriptionReviewClip[],
) {
  return Object.fromEntries(
    transcriptionFilters.map(({ value }) => [
      value,
      clips.filter((clip) => matchesTranscriptionFilter(clip, value)).length,
    ]),
  ) as Record<TranscriptionFilter, number>;
}

/** A dash for a model with no price configured. */
export function formatCost(usd: number | null) {
  if (usd === null) return "–";
  return usd < 0.01 && usd > 0 ? "<$0.01" : `$${usd.toFixed(2)}`;
}
