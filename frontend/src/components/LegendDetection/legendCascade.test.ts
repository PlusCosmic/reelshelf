import { describe, expect, it } from "vitest";
import type { LegendCascadeClip } from "@/shared/services/legendDetection";
import { escalates, simulateCascade } from "./legendCascade";

function clip(overrides: Partial<LegendCascadeClip> = {}): LegendCascadeClip {
  return {
    clipId: "clip",
    labelLegend: "Wraith",
    firstStatus: "succeeded",
    firstLegend: "Wraith",
    firstConfidence: 0.95,
    firstHudDetected: true,
    firstDurationMs: 4000,
    firstCostUsd: 0.001,
    escalationStatus: "succeeded",
    escalationLegend: "Wraith",
    escalationDurationMs: 2000,
    escalationCostUsd: 0.02,
    ...overrides,
  };
}

describe("escalates", () => {
  it("escalates an unsure or missing legend when the HUD was seen", () => {
    expect(escalates(clip({ firstConfidence: 0.7 }), 0.9)).toBe(true);
    expect(escalates(clip({ firstLegend: null }), 0.9)).toBe(true);
    expect(escalates(clip({ firstConfidence: 0.95 }), 0.9)).toBe(false);
  });

  it("never escalates a missing HUD or a failed run, as production doesn't", () => {
    expect(
      escalates(clip({ firstHudDetected: false, firstLegend: null }), 0.9),
    ).toBe(false);
    expect(
      escalates(clip({ firstStatus: "failed", firstLegend: null }), 0.9),
    ).toBe(false);
  });
});

describe("simulateCascade", () => {
  const clips = [
    clip(),
    clip({ firstLegend: "Ash", firstConfidence: 0.6 }),
    clip({ firstLegend: "Ash", firstConfidence: 0.95 }),
    clip({ firstStatus: "failed", firstLegend: null, firstConfidence: null }),
  ];

  it("takes the second model's answer only for escalated clips", () => {
    const result = simulateCascade(clips, 0.9);
    expect(result.escalated).toBe(1);
    expect(result.correct).toBe(2);
    expect(result.costUsd).toBeCloseTo(4 * 0.001 + 0.02);
    expect(result.meanLatencyMs).toBeCloseTo((4 * 4000 + 2000) / 4);
  });

  it("rescues more clips at a higher threshold", () => {
    expect(simulateCascade(clips, 1).correct).toBe(3);
  });

  it("has no cost while a run it needs has none", () => {
    expect(
      simulateCascade(
        [clip({ firstConfidence: 0.5, escalationCostUsd: null })],
        0.9,
      ).costUsd,
    ).toBeNull();
    expect(
      simulateCascade([clip({ escalationCostUsd: null })], 0.9).costUsd,
    ).toBeCloseTo(0.001);
  });
});
