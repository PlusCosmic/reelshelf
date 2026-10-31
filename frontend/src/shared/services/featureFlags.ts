import { createUserApi } from "./apiClients";

/**
 * Every feature flag the frontend checks (ADR-0009), mirroring `FeatureFlagKeys` in the API. A flag is temporary:
 * removing its key here makes every `useFeature` call for it a type error, so the gated code goes with it.
 * Add a key here and in `FeatureFlagKeys.All` together.
 */
export const featureFlagKeys = [
  /** Buying a paid plan; the API folds it into `billing_enabled` and the plans' prices, so nothing checks it here yet. */
  "paid-plans",
] as const;

export type FeatureFlagKey = (typeof featureFlagKeys)[number];

/** The keys of the flags on for the signed-in account; the API alone decides, so the browser can't turn one on. */
export async function fetchFeatures(): Promise<string[]> {
  const features = await createUserApi().getMyFeatures();
  return features.enabled;
}

/** Off until the flags have loaded, and off if they can't be: a flagged feature never flashes in and out. */
export function isFeatureOn(
  enabled: readonly string[] | undefined,
  key: FeatureFlagKey,
): boolean {
  return enabled?.includes(key) ?? false;
}
