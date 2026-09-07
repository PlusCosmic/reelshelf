import type { StorageUsage } from "@/shared/services/user";
import { formatFileSize } from "@/shared/utils/format";

export function StorageMeter({
  usage,
  compact = false,
}: {
  usage: StorageUsage | undefined;
  compact?: boolean;
}) {
  if (!usage) return null;

  const used = formatFileSize(usage.usedBytes);
  const limit = usage.limitBytes ?? null;
  const fraction =
    limit && limit > 0 ? Math.min(1, usage.usedBytes / limit) : 0;
  const nearLimit = limit !== null && fraction >= 0.9;
  const label = limit
    ? `${used} of ${formatFileSize(limit)} used`
    : `${used} used · Unlimited storage`;

  return (
    <div
      className={`rs-storage-meter${compact ? " compact" : ""}${
        nearLimit ? " near-limit" : ""
      }`}
      role="meter"
      aria-label="Clip storage"
      aria-valuemin={0}
      aria-valuemax={limit ?? undefined}
      aria-valuenow={usage.usedBytes}
      aria-valuetext={label}
    >
      <span className="rs-storage-meter-label">{label}</span>
      {limit ? (
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
