import type { Clip } from "@/api-client";
import { formatDuration, formatSize } from "../reelshelf-model";

export function StatLine({ clips }: { clips: Clip[] }) {
  const totalSeconds = clips.reduce(
    (sum, clip) => sum + (clip.video.length || 0),
    0,
  );
  const totalBytes = clips.reduce(
    (sum, clip) => sum + (clip.video.storageSize || 0),
    0,
  );
  return (
    <span>
      {clips.length} clips - {formatDuration(totalSeconds)} -{" "}
      {formatSize(totalBytes)}
    </span>
  );
}
