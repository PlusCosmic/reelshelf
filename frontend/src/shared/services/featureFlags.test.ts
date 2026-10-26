import { describe, expect, it } from "vitest";
import { isFeatureOn, type FeatureFlagKey } from "./featureFlags";

// No flags exist yet, so FeatureFlagKey is `never`; a stand-in key keeps the behaviour under test.
const beta = "beta" as FeatureFlagKey;

describe("isFeatureOn", () => {
  it("is off until the flags have loaded", () => {
    expect(isFeatureOn(undefined, beta)).toBe(false);
  });

  it("is on only when the API listed the flag", () => {
    expect(isFeatureOn(["beta"], beta)).toBe(true);
    expect(isFeatureOn(["other"], beta)).toBe(false);
    expect(isFeatureOn([], beta)).toBe(false);
  });
});
