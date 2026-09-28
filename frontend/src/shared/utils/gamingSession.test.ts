import { describe, expect, it } from "vitest";
import { gamingSessionBounds, gamingSessionDate } from "./gamingSession";

describe("gaming sessions", () => {
  it("counts the small hours as the night before", () => {
    expect(gamingSessionDate(new Date(2026, 0, 11, 2, 30))).toBe("2026-01-10");
    expect(gamingSessionDate(new Date(2026, 0, 11, 5, 0))).toBe("2026-01-11");
  });

  it("runs from 5am on the session's day to 5am the next", () => {
    const { day, start, end } = gamingSessionBounds(
      new Date(2026, 0, 11, 2, 30),
    );
    expect(day).toEqual(new Date(2026, 0, 10));
    expect(start).toEqual(new Date(2026, 0, 10, 5));
    expect(end).toEqual(new Date(2026, 0, 11, 5));
  });

  it("puts an evening clip in that evening's session", () => {
    const { start, end } = gamingSessionBounds(new Date(2026, 0, 10, 21, 50));
    expect(start).toEqual(new Date(2026, 0, 10, 5));
    expect(end).toEqual(new Date(2026, 0, 11, 5));
  });
});
