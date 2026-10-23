import { type AccountPlan, tierNames } from "@/shared/services/plans";

/** How many clips the account keeps against its plan's limit; the CSS is the old storage meter's. */
export function PlanMeter({
  plan,
  compact = false,
}: {
  plan: AccountPlan | undefined;
  compact?: boolean;
}) {
  if (!plan) return null;

  const tier = tierNames[plan.tier] ?? plan.tier;
  const limit = plan.clipLimit ?? null;
  const fraction =
    limit !== null && limit > 0 ? Math.min(1, plan.clipCount / limit) : 0;
  const nearLimit = limit !== null && (limit === 0 || fraction >= 0.9);
  const clips = plan.clipCount.toLocaleString();
  const label =
    limit !== null
      ? `${clips} of ${limit.toLocaleString()} clips · ${tier}`
      : `${clips} ${plan.clipCount === 1 ? "clip" : "clips"} · ${tier}`;

  return (
    <div
      className={`rs-storage-meter${compact ? " compact" : ""}${
        nearLimit ? " near-limit" : ""
      }`}
      role="meter"
      aria-label="Clips kept"
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
