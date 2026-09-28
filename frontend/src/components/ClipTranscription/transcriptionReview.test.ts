import { describe, expect, it } from "vitest";
import type {
  ClipTranscriptionReviewClip,
  ClipTranscriptionRun,
} from "@/shared/services/clipTranscription";
import {
  countByTranscriptionFilter,
  formatCost,
  transcriptOutcome,
} from "./transcriptionReview";

function run(overrides: Partial<ClipTranscriptionRun>): ClipTranscriptionRun {
  return {
    id: "run",
    clipId: "clip",
    trigger: "auto",
    model: "gpt-transcribe",
    promptVersion: null,
    prompt: null,
    keywords: [],
    status: "succeeded",
    attempts: 1,
    hasAudio: true,
    audioSeconds: 30,
    audioBytes: 1000,
    transcript: "push him",
    languages: ["en"],
    inputTokens: null,
    outputTokens: null,
    durationMs: null,
    error: null,
    createdAt: new Date(),
    completedAt: new Date(),
    ...overrides,
  };
}

function clip(
  latestRun: ClipTranscriptionRun | null,
): ClipTranscriptionReviewClip {
  return {
    clipId: "clip",
    title: "Clip",
    ownerName: "Owner",
    gameName: "Apex Legends",
    gameSlug: "apex-legends",
    createdAt: new Date(),
    lengthSeconds: 30,
    embedUrl: "",
    thumbnailUrl: "",
    latestRun,
    runCount: latestRun ? 1 : 0,
  };
}

describe("transcriptOutcome", () => {
  it("tells silent clips apart from clips without an audio track", () => {
    expect(transcriptOutcome(run({ transcript: "" }))).toBe("silent");
    expect(transcriptOutcome(run({ hasAudio: false, transcript: "" }))).toBe(
      "no-audio",
    );
    expect(transcriptOutcome(run({}))).toBe("transcribed");
    expect(transcriptOutcome(run({ status: "running" }))).toBe("queued");
    expect(transcriptOutcome(null)).toBe("not-run");
  });
});

describe("countByTranscriptionFilter", () => {
  it("counts clips without audio as no speech", () => {
    const counts = countByTranscriptionFilter([
      clip(run({})),
      clip(run({ transcript: "" })),
      clip(run({ hasAudio: false, transcript: "" })),
      clip(run({ status: "failed" })),
      clip(null),
    ]);
    expect(counts).toEqual({
      all: 5,
      transcribed: 1,
      silent: 2,
      failed: 1,
      "not-run": 1,
    });
  });
});

describe("formatCost", () => {
  it("shows fractions of a cent as under a cent", () => {
    expect(formatCost(0.0045)).toBe("<$0.01");
    expect(formatCost(0)).toBe("$0.00");
    expect(formatCost(1.234)).toBe("$1.23");
    expect(formatCost(null)).toBe("–");
  });
});
