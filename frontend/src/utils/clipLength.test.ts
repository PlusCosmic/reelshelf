import { describe, expect, it } from "vitest";
import { clipLengthProblem } from "./clipLength";

describe("clipLengthProblem", () => {
  it("allows clips up to the cap, with a second for rounding", () => {
    expect(clipLengthProblem(60, 60, true)).toBeNull();
    expect(clipLengthProblem(60.9, 60, true)).toBeNull();
    expect(clipLengthProblem(61, 60, true)).toBeNull();
  });

  it("refuses a longer clip in the API's words, offering an upgrade only when one keeps longer clips", () => {
    expect(clipLengthProblem(95.4, 60, true)).toBe(
      "This clip is 95 s. Your plan keeps clips up to 1 min, so trim it or upgrade for clips up to 2 min.",
    );
    expect(clipLengthProblem(150, 120, true)).toBe(
      "This clip is 150 s, and clips can be up to 2 min. Trim it, then add it again.",
    );
  });

  it("offers no upgrade when this account can't buy a plan", () => {
    expect(clipLengthProblem(95.4, 60, false)).toBe(
      "This clip is 95 s, and clips can be up to 1 min. Trim it, then add it again.",
    );
  });

  it("leaves unknown lengths and uncapped plans to the API", () => {
    expect(clipLengthProblem(null, 60, true)).toBeNull();
    expect(clipLengthProblem(3600, null, true)).toBeNull();
  });
});
