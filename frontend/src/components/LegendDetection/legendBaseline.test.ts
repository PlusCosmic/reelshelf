import { describe, expect, it } from "vitest";
import type { LegendBaselineClip } from "@/shared/services/legendDetection";
import {
  answerOf,
  buildGroups,
  cantTellKey,
  confirmLabels,
  matchLegends,
  noResultKey,
} from "./legendBaseline";

let next = 0;
function clip(
  answer: {
    label?: string | null;
    result?: string | null;
    confidence?: number;
  },
  id = `clip-${++next}`,
): LegendBaselineClip {
  return {
    clipId: id,
    title: id,
    ownerName: "Owner",
    createdAt: new Date(2026, 8, next),
    frameUrls: [],
    label:
      answer.label === undefined
        ? null
        : { playerLegend: answer.label, labelledAt: new Date(2026, 9, 1) },
    result:
      answer.result === undefined
        ? null
        : {
            playerLegend: answer.result,
            confidence: answer.confidence ?? 0.95,
            provider: "openai",
            model: "gpt-6-luna",
            hudDetected: answer.result !== null,
          },
  };
}

describe("answerOf", () => {
  it("prefers the label, then the current result", () => {
    expect(answerOf(clip({ label: "Ash", result: "Wraith" }))).toEqual({
      answer: "Ash",
      source: "label",
      confidence: null,
    });
    expect(answerOf(clip({ result: "Wraith", confidence: 0.6 }))).toEqual({
      answer: "Wraith",
      source: "result",
      confidence: 0.6,
    });
    expect(answerOf(clip({}))).toEqual({
      answer: null,
      source: "none",
      confidence: null,
    });
  });

  it("keeps a can't-tell label rather than falling back to the result", () => {
    expect(answerOf(clip({ label: null, result: "Wraith" })).answer).toBeNull();
  });
});

describe("buildGroups", () => {
  it("puts unanswered and can't-tell clips first, then the least confident group", () => {
    const groups = buildGroups(
      [
        clip({ result: "Wraith", confidence: 0.99 }),
        clip({ result: "Bloodhound", confidence: 0.62 }),
        clip({ result: null }),
        clip({}),
      ],
      new Map(),
      false,
    );
    expect(groups.map((group) => group.key)).toEqual([
      noResultKey,
      cantTellKey,
      "Bloodhound",
      "Wraith",
    ]);
  });

  it("hides confirmed clips and finished groups unless asked", () => {
    const clips = [
      clip({ label: "Wraith", result: "Wraith" }),
      clip({ result: "Wraith" }),
      clip({ label: "Ash" }),
    ];
    const hidden = buildGroups(clips, new Map(), false);
    expect(hidden.map((group) => group.key)).toEqual(["Wraith"]);
    expect(hidden[0].tiles).toHaveLength(1);
    expect(hidden[0].total).toBe(2);

    const shown = buildGroups(clips, new Map(), true);
    expect(shown.map((group) => group.key)).toEqual(["Wraith", "Ash"]);
    expect(shown[1].done).toBe(true);
  });

  it("keeps a relabelled clip in the group it was found in, and leaves it out of a confirm", () => {
    const moved = clip({ label: "Valkyrie", result: "Bloodhound" });
    const other = clip({ result: "Bloodhound", confidence: 0.9 });
    const [group] = buildGroups(
      [moved, other],
      new Map([[moved.clipId, "Bloodhound"]]),
      false,
    );
    expect(group.key).toBe("Bloodhound");
    expect(group.tiles.map((tile) => [tile.clip.clipId, tile.moved])).toEqual([
      [other.clipId, false],
      [moved.clipId, true],
    ]);
    expect(confirmLabels(group)).toEqual([
      { clipId: other.clipId, playerLegend: "Bloodhound" },
    ]);
  });

  it("orders a group's clips least confident first", () => {
    const sure = clip({ result: "Octane", confidence: 0.99 });
    const unsure = clip({ result: "Octane", confidence: 0.55 });
    const [group] = buildGroups([sure, unsure], new Map(), false);
    expect(group.tiles.map((tile) => tile.clip.clipId)).toEqual([
      unsure.clipId,
      sure.clipId,
    ]);
    expect(group.lowestConfidence).toBe(0.55);
  });
});

describe("matchLegends", () => {
  const legends = ["Mad Maggie", "Mirage", "Valkyrie", "Revenant", "Wraith"];

  it("matches the start of any word before matches inside a name", () => {
    expect(matchLegends(legends, "mag")).toEqual(["Mad Maggie"]);
    expect(matchLegends(legends, "ra")).toEqual(["Mirage", "Wraith"]);
    expect(matchLegends(legends, "re")).toEqual(["Revenant"]);
  });

  it("returns every legend for an empty query", () => {
    expect(matchLegends(legends, "  ")).toEqual(legends);
  });
});
