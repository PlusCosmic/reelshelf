import type {
  LegendDetectionReviewClip,
  LegendDetectionRun,
  LegendDetectionUsage,
} from "@/shared/services/legendDetection";
import { isRunActive } from "@/shared/services/legendDetection";

/** Below this, a detection is worth checking by eye. Matches the prompt's bar for a confident answer. */
export const reviewConfidence = 0.9;

export type ReviewFilter =
  "all" | "needs-review" | "confident" | "failed" | "active" | "not-run";

export const reviewFilters: Array<{ value: ReviewFilter; label: string }> = [
  { value: "all", label: "All" },
  { value: "needs-review", label: "Needs review" },
  { value: "confident", label: "Confident" },
  { value: "failed", label: "Failed" },
  { value: "active", label: "In progress" },
  { value: "not-run", label: "Not run" },
];

/**
 * A succeeded run needs a look when the HUD was missed, any legend is missing, or any legend is below the
 * confidence bar. Names are not considered: they are read as text and matter less than the legend.
 */
export function needsReview(run: LegendDetectionRun) {
  if (!run.hudDetected || !run.playerLegend) return true;
  const confidences = [
    run.playerLegendConfidence ?? 0,
    ...run.teammates
      .filter((teammate) => teammate.legend !== null)
      .map((teammate) => teammate.legendConfidence),
  ];
  return confidences.some((confidence) => confidence < reviewConfidence);
}

export function matchesFilter(
  clip: LegendDetectionReviewClip,
  filter: ReviewFilter,
) {
  const run = clip.latestRun;
  switch (filter) {
    case "all":
      return true;
    case "not-run":
      return run === null;
    case "active":
      return isRunActive(run);
    case "failed":
      return run?.status === "failed";
    case "needs-review":
      return run?.status === "succeeded" && needsReview(run);
    case "confident":
      return run?.status === "succeeded" && !needsReview(run);
  }
}

export function countByFilter(clips: LegendDetectionReviewClip[]) {
  return Object.fromEntries(
    reviewFilters.map(({ value }) => [
      value,
      clips.filter((clip) => matchesFilter(clip, value)).length,
    ]),
  ) as Record<ReviewFilter, number>;
}

export type ConfidenceTone = "accent" | "neutral" | "danger";

export function confidenceTone(confidence: number | null): ConfidenceTone {
  if (confidence === null) return "danger";
  if (confidence >= reviewConfidence) return "accent";
  return confidence >= 0.6 ? "neutral" : "danger";
}

/** Green only when nothing needs a look; red when the player's own legend is missing or unlikely. */
export function reviewTone(run: LegendDetectionRun): ConfidenceTone {
  if (!needsReview(run)) return "accent";
  return confidenceTone(
    run.playerLegend ? run.playerLegendConfidence : null,
  ) === "danger"
    ? "danger"
    : "neutral";
}

export function formatConfidence(confidence: number | null) {
  return confidence === null ? "–" : `${Math.round(confidence * 100)}%`;
}

export function formatTokens(tokens: number | null) {
  if (tokens === null) return "–";
  return tokens >= 10_000
    ? `${(tokens / 1000).toFixed(0)}k`
    : tokens.toLocaleString();
}

/** Share of input tokens the provider served from its prompt cache. */
export function cachedShare(
  usage: Pick<LegendDetectionUsage, "inputTokens" | "cachedInputTokens">,
) {
  return usage.inputTokens > 0
    ? usage.cachedInputTokens / usage.inputTokens
    : 0;
}
