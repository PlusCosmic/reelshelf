const DAY_MS = 24 * 60 * 60 * 1000;

/** Days until a binned clip is deleted for good, rounded up: 30 just after binning, 1 on its last day, 0 once due. */
export function binDaysLeft(
  binnedAt: Date,
  retentionDays: number,
  now: Date = new Date(),
) {
  const deletesAt = binnedAt.getTime() + retentionDays * DAY_MS;
  return Math.max(0, Math.ceil((deletesAt - now.getTime()) / DAY_MS));
}
