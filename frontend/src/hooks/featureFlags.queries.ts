import { useQuery } from "@tanstack/react-query";
import {
  fetchFeatures,
  isFeatureOn,
  type FeatureFlagKey,
} from "@/shared/services/featureFlags";

export const featuresQueryKey = ["user", "me", "features"] as const;

/**
 * The flags on for the signed-in account. Only for the signed-in app, never the landing page. A change in PostHog
 * reaches the API within its 5-minute poll, so a minute's staleness here adds little.
 */
export function useFeatures(enabled = true) {
  return useQuery({
    queryKey: featuresQueryKey,
    queryFn: fetchFeatures,
    enabled,
    retry: false,
    staleTime: 60_000,
  });
}

/** Whether a flag is on for the signed-in account; false while loading or when the flags can't be read. */
export function useFeature(key: FeatureFlagKey): boolean {
  const { data } = useFeatures();
  return isFeatureOn(data, key);
}
