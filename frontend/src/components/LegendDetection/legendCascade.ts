import type { LegendCascadeClip } from "@/shared/services/legendDetection";

/** Thresholds the cascade table compares, beside the one picked on the slider. */
export const cascadeThresholds = [0.6, 0.7, 0.8, 0.85, 0.9, 0.95, 0.98, 1];

/**
 * Whether production would escalate the first model's answer at this threshold, as `ShouldEscalate` does: only a
 * run that succeeded and saw the owner's HUD, with no legend or one below the threshold. A failed run is not
 * escalated.
 */
export function escalates(clip: LegendCascadeClip, threshold: number) {
  return (
    clip.firstStatus === "succeeded" &&
    clip.firstHudDetected === true &&
    (clip.firstLegend === null || (clip.firstConfidence ?? 0) < threshold)
  );
}

export type CascadeResult = {
  threshold: number;
  clips: number;
  escalated: number;
  correct: number;
  /** Null while any run it needs has neither a billed cost nor a price. */
  costUsd: number | null;
  /** Mean time per clip: the first model's call, plus the second's when escalated. */
  meanLatencyMs: number | null;
};

/** What production would have got on these clips running the first model and escalating below `threshold`. */
export function simulateCascade(
  clips: LegendCascadeClip[],
  threshold: number,
): CascadeResult {
  let escalated = 0;
  let correct = 0;
  let cost: number | null = 0;
  let latency = 0;
  let timed = 0;
  for (const clip of clips) {
    const up = escalates(clip, threshold);
    const status = up ? clip.escalationStatus : clip.firstStatus;
    const legend = up ? clip.escalationLegend : clip.firstLegend;
    if (up) escalated += 1;
    if (status === "succeeded" && legend === clip.labelLegend) correct += 1;

    const parts = up
      ? [clip.firstCostUsd, clip.escalationCostUsd]
      : [clip.firstCostUsd];
    cost =
      cost === null || parts.some((part) => part === null)
        ? null
        : cost + parts.reduce<number>((sum, part) => sum + (part ?? 0), 0);

    if (clip.firstDurationMs !== null) {
      latency +=
        clip.firstDurationMs + (up ? (clip.escalationDurationMs ?? 0) : 0);
      timed += 1;
    }
  }
  return {
    threshold,
    clips: clips.length,
    escalated,
    correct,
    costUsd: cost,
    meanLatencyMs: timed > 0 ? latency / timed : null,
  };
}
