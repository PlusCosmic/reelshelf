import type {
  LegendDetectionLabel,
  LegendDetectionReviewClip,
  LegendDetectionRun,
  LegendDetectionUsage,
} from "@/shared/services/legendDetection";
import { isRunActive } from "@/shared/services/legendDetection";

/** Below this, a detection is worth checking by eye. Matches the prompt's bar for a confident answer. */
export const reviewConfidence = 0.9;

export type ReviewFilter =
  | "all"
  | "needs-review"
  | "confident"
  | "failed"
  | "active"
  | "not-run"
  | "unlabelled"
  | "wrong";

export const reviewFilters: Array<{ value: ReviewFilter; label: string }> = [
  { value: "all", label: "All" },
  { value: "needs-review", label: "Needs review" },
  { value: "confident", label: "Confident" },
  { value: "failed", label: "Failed" },
  { value: "active", label: "In progress" },
  { value: "not-run", label: "Not run" },
  { value: "unlabelled", label: "Unlabelled" },
  { value: "wrong", label: "Labelled wrong" },
];

/** The detected legends as a label: the owner's legend and every teammate legend that was identified. */
export function labelFromRun(run: LegendDetectionRun) {
  return {
    playerLegend: run.playerLegend,
    teammateLegends: run.teammates
      .map((teammate) => teammate.legend)
      .filter((legend): legend is string => legend !== null),
  };
}

function sameLegends(a: string[], b: string[]) {
  const sortedA = [...a].sort();
  const sortedB = [...b].sort();
  return (
    sortedA.length === sortedB.length &&
    sortedA.every((legend, index) => legend === sortedB[index])
  );
}

/**
 * Whether a succeeded run matches the clip's label: the owner's legend exactly and teammates as an unordered
 * set, the same rule the usage table scores by. Null when there is nothing to compare.
 */
export function labelVerdict(
  run: LegendDetectionRun | null,
  label: LegendDetectionLabel | null,
): "correct" | "wrong" | null {
  if (!run || run.status !== "succeeded" || !label) return null;
  const detected = labelFromRun(run);
  return detected.playerLegend === label.playerLegend &&
    sameLegends(detected.teammateLegends, label.teammateLegends)
    ? "correct"
    : "wrong";
}

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
    case "unlabelled":
      return run?.status === "succeeded" && clip.label === null;
    case "wrong":
      return labelVerdict(run, clip.label) === "wrong";
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

/** "92%" for 46 correct of 50 labelled runs; a dash until anything is labelled. */
export function formatAccuracy(correct: number, labelled: number) {
  return labelled > 0 ? `${Math.round((correct / labelled) * 100)}%` : "–";
}

/** Share of input tokens the provider served from its prompt cache. */
export function cachedShare(
  usage: Pick<LegendDetectionUsage, "inputTokens" | "cachedInputTokens">,
) {
  return usage.inputTokens > 0
    ? usage.cachedInputTokens / usage.inputTokens
    : 0;
}

/** A run's reasoning effort, where null is the model's default. */
export function formatReasoningEffort(effort: string | null | undefined) {
  return effort ?? "default";
}
