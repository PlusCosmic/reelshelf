import { planCopy } from "@/copy/plan";
import { type AccountPlan, tierNames } from "@/shared/services/plans";

/** At this share of the limit the meter turns amber and the account is "nearly full". */
export const nearLimitFraction = 0.9;

/**
 * How many clips the account keeps against its plan's limit; the CSS is the old storage meter's. Settings names the
 * plan above it (`showTier` false); the compact one in the profile menu names it in the label.
 */
export function PlanMeter({
  plan,
  compact = false,
  showTier = true,
}: {
  plan: AccountPlan | undefined;
  compact?: boolean;
  showTier?: boolean;
}) {
  if (!plan) return null;

  const tier = tierNames[plan.tier] ?? plan.tier;
  const limit = plan.clipLimit ?? null;
  const fraction =
    limit !== null && limit > 0 ? Math.min(1, plan.clipCount / limit) : 0;
  // Over the limit (clips will be deleted) and nearly full (new clips soon refused) are told apart, in words as
  // well as colour.
  const overLimit = limit !== null && plan.clipCount > limit;
  const nearLimit =
    limit !== null &&
    !overLimit &&
    (limit === 0 || fraction >= nearLimitFraction);
  let label: string;
  if (limit === null) {
    label = planCopy.meter.noLimit(plan.clipCount);
  } else if (overLimit) {
    const over = plan.clipCount - limit;
    label = compact
      ? planCopy.meter.overCompact(plan.clipCount, over)
      : planCopy.meter.over(plan.clipCount, over, tier, limit);
  } else {
    label = planCopy.meter.ofLimit(plan.clipCount, limit);
    if (nearLimit && compact) label += planCopy.meter.nearlyFull;
  }
  if (showTier && !(overLimit && !compact)) label += planCopy.meter.tier(tier);

  return (
    <div
      className={`rs-storage-meter${compact ? " compact" : ""}${
        nearLimit ? " near-limit" : ""
      }${overLimit ? " over-limit" : ""}`}
      role="meter"
      aria-label={planCopy.meter.label}
      aria-valuemin={0}
      aria-valuemax={limit ?? undefined}
      aria-valuenow={plan.clipCount}
      aria-valuetext={label}
    >
      <span className="rs-storage-meter-label">{label}</span>
      {limit !== null ? (
        <span className="rs-storage-meter-track" aria-hidden="true">
          <span
            className="rs-storage-meter-fill"
            style={{ width: `${Math.round(fraction * 100)}%` }}
          />
        </span>
      ) : null}
    </div>
  );
}
