import { describe, expect, it } from "vitest";
import type {
  LegendDetectionReviewClip,
  LegendDetectionRun,
} from "@/shared/services/legendDetection";
import {
  cachedShare,
  confidenceTone,
  countByFilter,
  formatConfidence,
  matchesFilter,
  needsReview,
  reviewTone,
} from "./legendReview";

function run(overrides: Partial<LegendDetectionRun> = {}): LegendDetectionRun {
  return {
    id: "run",
    clipId: "clip",
    trigger: "auto",
    provider: "openai",
    model: "gpt-6-luna",
    promptVersion: "abc",
    status: "succeeded",
    attempts: 1,
    frameCount: 6,
    hudDetected: true,
    playerLegend: "Horizon",
    playerLegendConfidence: 0.99,
    playerName: "Cosmic",
    playerNameConfidence: 0.99,
    teammates: [
      {
        slot: 1,
        name: "Jonesy",
        legend: "Gibraltar",
        nameConfidence: 0.5,
        legendConfidence: 0.98,
      },
    ],
    rawResponse: "{}",
    inputTokens: 17000,
    cachedInputTokens: 0,
    outputTokens: 200,
    durationMs: 5000,
    error: null,
    createdAt: new Date(),
    completedAt: new Date(),
    ...overrides,
  };
}

function clip(latestRun: LegendDetectionRun | null): LegendDetectionReviewClip {
  return {
    clipId: "clip",
    title: "Clip",
    createdAt: new Date(),
    lengthSeconds: 30,
    embedUrl: "https://player.example/embed",
    frameUrls: [],
    latestRun,
    runCount: latestRun ? 1 : 0,
  };
}

describe("needsReview", () => {
  it("passes a confident run even when a name is uncertain", () => {
    expect(needsReview(run())).toBe(false);
  });

  it("flags a missed HUD, a missing player legend, or a low-confidence legend", () => {
    expect(needsReview(run({ hudDetected: false }))).toBe(true);
    expect(needsReview(run({ playerLegend: null }))).toBe(true);
    expect(needsReview(run({ playerLegendConfidence: 0.7 }))).toBe(true);
    expect(
      needsReview(
        run({
          teammates: [
            {
              slot: 1,
              name: null,
              legend: "Wraith",
              nameConfidence: 0,
              legendConfidence: 0.4,
            },
          ],
        }),
      ),
    ).toBe(true);
  });

  it("ignores teammates with no legend, such as an empty slot", () => {
    expect(
      needsReview(
        run({
          teammates: [
            {
              slot: 2,
              name: null,
              legend: null,
              nameConfidence: 0,
              legendConfidence: 0,
            },
          ],
        }),
      ),
    ).toBe(false);
  });
});

describe("filters", () => {
  const clips = [
    clip(run()),
    clip(run({ playerLegendConfidence: 0.5 })),
    clip(run({ status: "failed", hudDetected: null, playerLegend: null })),
    clip(run({ status: "pending", hudDetected: null, playerLegend: null })),
    clip(null),
  ];

  it("places every clip under exactly one status filter", () => {
    expect(countByFilter(clips)).toEqual({
      all: 5,
      "needs-review": 1,
      confident: 1,
      failed: 1,
      active: 1,
      "not-run": 1,
    });
  });

  it("does not treat an in-progress run as needing review", () => {
    expect(matchesFilter(clips[3], "needs-review")).toBe(false);
  });
});

describe("reviewTone", () => {
  it("is green only when nothing needs review", () => {
    expect(reviewTone(run())).toBe("accent");
    expect(
      reviewTone(
        run({
          teammates: [
            {
              slot: 1,
              name: null,
              legend: "Wraith",
              nameConfidence: 0,
              legendConfidence: 0.5,
            },
          ],
        }),
      ),
    ).toBe("neutral");
    expect(reviewTone(run({ playerLegend: null }))).toBe("danger");
    expect(reviewTone(run({ playerLegendConfidence: 0.3 }))).toBe("danger");
  });
});

describe("formatting", () => {
  it("formats confidence and picks a tone", () => {
    expect(formatConfidence(0.987)).toBe("99%");
    expect(formatConfidence(null)).toBe("–");
    expect(confidenceTone(0.95)).toBe("accent");
    expect(confidenceTone(0.7)).toBe("neutral");
    expect(confidenceTone(0.3)).toBe("danger");
  });

  it("reports the cached share of input tokens", () => {
    expect(
      cachedShare({ inputTokens: 20_000, cachedInputTokens: 15_000 }),
    ).toBe(0.75);
    expect(cachedShare({ inputTokens: 0, cachedInputTokens: 0 })).toBe(0);
  });
});
