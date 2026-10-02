import { describe, expect, it } from "vitest";
import { correctDrift, expectedPosition, type RoomPlayback } from "./watchRoom";

const playback = (overrides: Partial<RoomPlayback> = {}): RoomPlayback => ({
  clip: {
    clipId: "c",
    videoId: "v",
    title: "Triple",
    game: "Apex Legends",
    ownerName: "Alice",
    durationSeconds: 30,
  },
  playing: true,
  positionSeconds: 10,
  updatedAt: "2026-10-02T20:00:00.000Z",
  ...overrides,
});

const at = (iso: string) => Date.parse(iso);

describe("expectedPosition", () => {
  it("adds the time since the update while playing", () => {
    expect(
      expectedPosition(playback(), 0, at("2026-10-02T20:00:02.500Z")),
    ).toBeCloseTo(12.5);
  });

  it("stays put while paused", () => {
    expect(
      expectedPosition(
        playback({ playing: false }),
        0,
        at("2026-10-02T20:00:09Z"),
      ),
    ).toBe(10);
  });

  it("uses server time, so a local clock running behind still lands in step", () => {
    // Local clock reads 20:00:00 but the server is 3s ahead.
    expect(
      expectedPosition(playback(), 3_000, at("2026-10-02T20:00:00Z")),
    ).toBeCloseTo(13);
  });

  it("stops at the end of the clip", () => {
    expect(expectedPosition(playback(), 0, at("2026-10-02T20:05:00Z"))).toBe(
      30,
    );
  });
});

describe("correctDrift", () => {
  it("leaves small drift alone", () => {
    expect(correctDrift(10, 10.1)).toEqual({ kind: "none" });
  });

  it("speeds up a little when behind and slows down when ahead", () => {
    expect(correctDrift(10, 10.4)).toEqual({ kind: "rate", playbackRate: 1.1 });
    expect(correctDrift(10.3, 10)).toMatchObject({ kind: "rate" });
    const ahead = correctDrift(10.3, 10);
    expect(ahead.kind === "rate" && ahead.playbackRate).toBeCloseTo(0.9);
  });

  it("seeks when too far out to catch up smoothly", () => {
    expect(correctDrift(4, 10)).toEqual({ kind: "seek", to: 10 });
  });
});
