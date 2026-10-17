import { describe, expect, it } from "vitest";
import type {
  LegendEval,
  LegendEvalMiss,
} from "@/shared/services/legendDetection";
import {
  accuracy,
  confusionPairs,
  costPerThousand,
  estimateEval,
  formatWall,
  wilsonInterval,
} from "./legendEvals";

function evaluation(overrides: Partial<LegendEval> = {}): LegendEval {
  return {
    id: "eval",
    provider: "anthropic",
    model: "claude-haiku-5-5",
    reasoningEffort: "low",
    createdAt: new Date(2026, 9, 17),
    runs: 340,
    succeeded: 338,
    failed: 2,
    queued: 0,
    scored: 340,
    correct: 320,
    confidentMistakes: 4,
    inputTokens: 0,
    cachedInputTokens: 0,
    cacheWriteInputTokens: 0,
    outputTokens: 0,
    costUsd: 1.69,
    costBilled: false,
    p50DurationMs: 4000,
    p95DurationMs: 9000,
    wallSeconds: 680,
    promptVersions: ["abc"],
    ...overrides,
  };
}

function miss(
  label: string | null,
  said: string | null,
  status = "succeeded",
): LegendEvalMiss {
  return {
    runId: `${label}-${said}-${Math.random()}`,
    clipId: "clip",
    title: "Clip",
    frameUrls: [],
    status,
    playerLegend: said,
    confidence: 0.9,
    hudDetected: true,
    error: null,
    labelLegend: label,
  };
}

describe("wilsonInterval", () => {
  it("brackets the observed share and narrows as n grows", () => {
    const [low, high] = wilsonInterval(320, 340);
    expect(low).toBeLessThan(320 / 340);
    expect(high).toBeGreaterThan(320 / 340);
    const [wideLow, wideHigh] = wilsonInterval(32, 34);
    expect(wideHigh - wideLow).toBeGreaterThan(high - low);
  });

  it("stays inside 0–1 at the extremes", () => {
    expect(wilsonInterval(10, 10)[1]).toBeLessThanOrEqual(1);
    expect(wilsonInterval(0, 10)[0]).toBeGreaterThanOrEqual(0);
    expect(wilsonInterval(0, 0)).toEqual([0, 1]);
  });
});

describe("accuracy and cost", () => {
  it("scores correct out of scored, and is null until something is scored", () => {
    expect(accuracy(evaluation())).toBeCloseTo(320 / 340);
    expect(accuracy(evaluation({ scored: 0, correct: 0 }))).toBeNull();
  });

  it("scales cost to 1,000 clips from the succeeded runs", () => {
    expect(costPerThousand(evaluation())).toBeCloseTo(5);
    expect(costPerThousand(evaluation({ costUsd: null }))).toBeNull();
  });
});

describe("estimateEval", () => {
  it("scales the last finished eval of the same model to the baseline", () => {
    const estimate = estimateEval(
      [evaluation()],
      "anthropic",
      "claude-haiku-5-5",
      676,
    );
    expect(estimate?.costUsd).toBeCloseTo(3.38);
    expect(estimate?.wallSeconds).toBe(1352);
  });

  it("has nothing to go on for a new model or a running eval", () => {
    expect(
      estimateEval([evaluation()], "anthropic", "claude-sonnet-5-5", 340),
    ).toBeNull();
    expect(
      estimateEval(
        [evaluation({ queued: 3 })],
        "anthropic",
        "claude-haiku-5-5",
        340,
      ),
    ).toBeNull();
  });
});

describe("confusionPairs", () => {
  it("counts label → answer pairs, most frequent first, leaving failures out", () => {
    const pairs = confusionPairs([
      miss("Valkyrie", "Bloodhound"),
      miss("Ash", "Wraith"),
      miss("Valkyrie", "Bloodhound"),
      miss("Octane", null, "failed"),
      miss(null, "Wraith"),
    ]);
    expect(pairs[0]).toEqual({
      label: "Valkyrie",
      said: "Bloodhound",
      count: 2,
    });
    expect(pairs).toHaveLength(3);
  });
});

describe("formatWall", () => {
  it("reads as seconds, minutes or hours", () => {
    expect(formatWall(45)).toBe("45s");
    expect(formatWall(680)).toBe("11m");
    expect(formatWall(6000)).toBe("1h 40m");
    expect(formatWall(null)).toBe("—");
  });
});
