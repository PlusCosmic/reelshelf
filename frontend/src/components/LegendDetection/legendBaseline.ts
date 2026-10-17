import type { LegendBaselineClip } from "@/shared/services/legendDetection";

/** Clips no run has answered yet, which need a legend picked by hand. */
export const noResultKey = "__no-result";
/** Clips whose owner's legend can't be identified, by label or by the current result. */
export const cantTellKey = "__cant-tell";

/** Results below this are flagged on the page, matching the review page's confident-mistake threshold. */
export const lowConfidenceBelow = 0.8;

/** Where a clip's answer comes from: its saved label, the current result, or neither yet. */
export type AnswerSource = "label" | "result" | "none";

export type BaselineTile = {
  clip: LegendBaselineClip;
  /** The saved label, or the current result's legend until the clip is labelled; null is can't tell. */
  answer: string | null;
  source: AnswerSource;
  /** The current result's confidence, shown until the clip is labelled. */
  confidence: number | null;
  confirmed: boolean;
  /** Labelled differently from its group during this visit, so it stays where it was found. */
  moved: boolean;
};

export type BaselineGroup = {
  key: string;
  /** The group's legend; null for the can't-tell and no-result groups. */
  legend: string | null;
  tiles: BaselineTile[];
  total: number;
  /** Tiles a confirm would save: shown, unlabelled and not already moved. */
  pending: BaselineTile[];
  movedCount: number;
  lowestConfidence: number | null;
  done: boolean;
};

export function answerOf(clip: LegendBaselineClip): {
  answer: string | null;
  source: AnswerSource;
  confidence: number | null;
} {
  if (clip.label)
    return {
      answer: clip.label.playerLegend,
      source: "label",
      confidence: null,
    };
  if (clip.result)
    return {
      answer: clip.result.playerLegend,
      source: "result",
      confidence: clip.result.confidence,
    };
  return { answer: null, source: "none", confidence: null };
}

export function groupKeyOf(answer: string | null, source: AnswerSource) {
  if (source === "none") return noResultKey;
  return answer ?? cantTellKey;
}

/**
 * Groups clips by their answer, so a page of one legend can be checked at a glance. Unconfirmed groups come first:
 * clips with no result, then can't tell, then the group with the least confident result. A clip relabelled during
 * this visit stays in the group it was found in (`origins`), so it doesn't jump away mid-scan. Confirmed clips are
 * shown only with `showConfirmed`; groups with nothing to show are left out.
 */
export function buildGroups(
  clips: LegendBaselineClip[],
  origins: ReadonlyMap<string, string>,
  showConfirmed: boolean,
): BaselineGroup[] {
  const byKey = new Map<string, BaselineTile[]>();
  for (const clip of clips) {
    const { answer, source, confidence } = answerOf(clip);
    const ownKey = groupKeyOf(answer, source);
    const key = origins.get(clip.clipId) ?? ownKey;
    const tile: BaselineTile = {
      clip,
      answer,
      source,
      confidence,
      confirmed: clip.label !== null,
      moved: key !== ownKey,
    };
    byKey.set(key, [...(byKey.get(key) ?? []), tile]);
  }

  const groups: BaselineGroup[] = [];
  for (const [key, all] of byKey) {
    const tiles = all
      .filter((tile) => showConfirmed || !tile.confirmed || tile.moved)
      .sort(
        (a, b) =>
          (a.confidence ?? 1) - (b.confidence ?? 1) ||
          a.clip.createdAt.getTime() - b.clip.createdAt.getTime(),
      );
    const pending = tiles.filter((tile) => !tile.confirmed);
    const confidences = pending
      .map((tile) => tile.confidence)
      .filter((value): value is number => value !== null);
    const done = all.every((tile) => tile.confirmed);
    if (tiles.length === 0 && !(showConfirmed && done)) continue;
    groups.push({
      key,
      legend: key === noResultKey || key === cantTellKey ? null : key,
      tiles,
      total: all.length,
      pending,
      movedCount: all.filter((tile) => tile.moved).length,
      lowestConfidence: confidences.length ? Math.min(...confidences) : null,
      done,
    });
  }

  const rank = (group: BaselineGroup) =>
    group.done
      ? 3
      : group.key === noResultKey
        ? 0
        : group.key === cantTellKey
          ? 1
          : 2;
  return groups.sort(
    (a, b) =>
      rank(a) - rank(b) ||
      (a.lowestConfidence ?? 1) - (b.lowestConfidence ?? 1) ||
      a.key.localeCompare(b.key),
  );
}

/** The labels that confirming a group saves: each shown, unlabelled clip with its answer as it stands. */
export function confirmLabels(group: BaselineGroup) {
  return group.pending.map((tile) => ({
    clipId: tile.clip.clipId,
    playerLegend: tile.answer,
  }));
}

/** Legends whose name, or a word in it, starts with the query; then any containing it. */
export function matchLegends(legends: string[], query: string) {
  const q = query.trim().toLowerCase();
  if (!q) return legends;
  const starts = legends.filter((legend) =>
    legend
      .toLowerCase()
      .split(" ")
      .some((word) => word.startsWith(q)),
  );
  const contains = legends.filter(
    (legend) => !starts.includes(legend) && legend.toLowerCase().includes(q),
  );
  return [...starts, ...contains];
}
