import { describe, expect, it } from "vitest";
import { clipLengthProblem } from "./clipLength";

describe("clipLengthProblem", () => {
  it("allows clips up to the cap, with a second for rounding", () => {
    expect(clipLengthProblem(60, 60)).toBeNull();
    expect(clipLengthProblem(60.9, 60)).toBeNull();
    expect(clipLengthProblem(61, 60)).toBeNull();
  });

  it("refuses a longer clip in the API's words", () => {
    expect(clipLengthProblem(95.4, 60)).toBe(
      "This clip is 95 s long, and your plan keeps clips of up to 1 min. Trim it, or upgrade for longer clips.",
    );
    expect(clipLengthProblem(180, 120)).toContain("This clip is 3 min long");
  });

  it("leaves unknown lengths and uncapped plans to the API", () => {
    expect(clipLengthProblem(null, 60)).toBeNull();
    expect(clipLengthProblem(3600, null)).toBeNull();
  });
});
