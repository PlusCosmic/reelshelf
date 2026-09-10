import { formatDuration, formatSize } from "../reelshelf-model";

export type ClipTotals = {
  clipCount: number;
  durationSeconds: number;
  storageBytes: number;
};

/**
 * Totals come from the API, which computes them over every clip row. Summing the clips in a
 * response would undercount: the library payload caps clips per category, and a clip Bunny has
 * not reported a storage size for yet only knows its declared file size, which the list omits.
 */
export function StatLine({ totals }: { totals: ClipTotals }) {
  return (
    <span>
      {totals.clipCount}&nbsp;clips - {formatDuration(totals.durationSeconds)} -{" "}
      {formatSize(totals.storageBytes)}
    </span>
  );
}
