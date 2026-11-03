import { describe, expect, it } from "vitest";
import { binDaysLeft } from "./binModel";

describe("binDaysLeft", () => {
  const now = new Date("2026-11-03T12:00:00Z");

  it("counts whole days until the bin lets a clip go", () => {
    expect(binDaysLeft(new Date("2026-11-03T11:00:00Z"), 30, now)).toBe(30);
    expect(binDaysLeft(new Date("2026-10-04T13:00:00Z"), 30, now)).toBe(1);
  });

  it("never goes below nothing", () => {
    expect(binDaysLeft(new Date("2026-10-01T00:00:00Z"), 30, now)).toBe(0);
  });
});
