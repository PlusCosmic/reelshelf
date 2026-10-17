import type {
  LegendEval,
  LegendEvalMiss,
} from "@/shared/services/legendDetection";

/**
 * The 95% Wilson interval for k correct out of n, which stays inside 0–1 and is honest at small n. At a few hundred
 * clips it is around ±3–5 points, so evals a point or two apart are effectively tied.
 */
export function wilsonInterval(k: number, n: number): [number, number] {
  if (n === 0) return [0, 1];
  const z = 1.96;
  const p = k / n;
  const denominator = 1 + (z * z) / n;
  const centre = (p + (z * z) / (2 * n)) / denominator;
  const half =
    (z * Math.sqrt((p * (1 - p)) / n + (z * z) / (4 * n * n))) / denominator;
  return [Math.max(0, centre - half), Math.min(1, centre + half)];
}

/** Share of scored runs that were right; null until a run has been scored. */
export function accuracy(evaluation: LegendEval) {
  return evaluation.scored > 0 ? evaluation.correct / evaluation.scored : null;
}

/** What 1,000 clips would cost at this eval's rate, from its succeeded runs. */
export function costPerThousand(evaluation: LegendEval) {
  if (evaluation.costUsd === null || evaluation.succeeded === 0) return null;
  return (evaluation.costUsd / evaluation.succeeded) * 1000;
}

export function formatUsd(value: number) {
  if (value === 0) return "$0";
  if (value < 0.01) return `$${value.toFixed(4)}`;
  if (value < 10) return `$${value.toFixed(2)}`;
  return `$${value.toFixed(1)}`;
}

export function formatSeconds(ms: number | null) {
  return ms === null ? "—" : `${(ms / 1000).toFixed(1)}s`;
}

export function formatWall(seconds: number | null) {
  if (seconds === null) return "—";
  if (seconds < 90) return `${seconds}s`;
  const minutes = Math.round(seconds / 60);
  return minutes < 90
    ? `${minutes}m`
    : `${Math.floor(minutes / 60)}h ${minutes % 60}m`;
}

export function evalName(evaluation: LegendEval) {
  return evaluation.reasoningEffort
    ? `${evaluation.model} · ${evaluation.reasoningEffort}`
    : evaluation.model;
}

/**
 * What an eval of this provider and model would cost and take on `clips` clips, from the most recent finished eval
 * of the same pair; null when there is none to go on.
 */
export function estimateEval(
  evals: LegendEval[],
  provider: string,
  model: string,
  clips: number,
) {
  const previous = evals.find(
    (evaluation) =>
      evaluation.provider === provider &&
      evaluation.model === model &&
      evaluation.queued === 0 &&
      evaluation.succeeded > 0,
  );
  if (!previous) return null;
  const perClip =
    previous.costUsd === null ? null : previous.costUsd / previous.succeeded;
  return {
    costUsd: perClip === null ? null : perClip * clips,
    wallSeconds:
      previous.wallSeconds === null
        ? null
        : Math.round((previous.wallSeconds / previous.runs) * clips),
  };
}

/** The most frequent label → answer mix-ups among an eval's wrong answers, failures left out. */
export function confusionPairs(misses: LegendEvalMiss[], limit = 6) {
  const counts = new Map<
    string,
    { label: string | null; said: string | null; count: number }
  >();
  for (const miss of misses) {
    if (miss.status !== "succeeded") continue;
    const key = `${miss.labelLegend ?? ""}→${miss.playerLegend ?? ""}`;
    const entry = counts.get(key) ?? {
      label: miss.labelLegend,
      said: miss.playerLegend,
      count: 0,
    };
    entry.count += 1;
    counts.set(key, entry);
  }
  return [...counts.values()].sort((a, b) => b.count - a.count).slice(0, limit);
}
