import type {
  ClipTranscriptionReviewClip,
  ClipTranscriptionRun,
} from "@/shared/services/clipTranscription";
import { clipTranscriptionCopy } from "@/copy/clipTranscription";
import { runsCopy } from "@/copy/runs";

export const transcriptionFilters = [
  { value: "all", label: runsCopy.filters.all },
  { value: "transcribed", label: clipTranscriptionCopy.outcomes.transcribed },
  { value: "silent", label: clipTranscriptionCopy.outcomes.silent },
  { value: "failed", label: runsCopy.filters.failed },
  { value: "not-run", label: runsCopy.filters.notRun },
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
  return usd < 0.01 && usd > 0
    ? runsCopy.units.lessThanCent
    : runsCopy.units.usd(usd.toFixed(2));
}
