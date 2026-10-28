/**
 * The plan limits copy quotes, mirroring `TierLimits` in `api/Plans/`. The signed-out pages and the
 * terms can't ask `/api/plans`, so they read these; change both together.
 */
export const planLimits = {
  free: { clips: 100, maxClipSeconds: 60 },
  plus: { clips: 1_000, maxClipSeconds: 120 },
  pro: { clips: 5_000, maxClipSeconds: 120 },
  /** In any 30 days an account may add this many times the clips its plan keeps. */
  fairUseMultiple: 2,
} as const;

export const formatCount = (count: number) => count.toLocaleString("en-GB");
