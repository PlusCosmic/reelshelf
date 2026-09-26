import type {
  DetectedTeammate,
  LegendDetectionReviewClip,
  LegendDetectionRun,
  LegendDetectionUsage,
} from "@/api-client";
import { createLegendDetectionApi } from "./apiClients";

export type {
  DetectedTeammate,
  LegendDetectionReviewClip,
  LegendDetectionRun,
  LegendDetectionUsage,
};

/** The signed-in admin's Apex Legends clips, each with its latest detection run. */
export async function fetchLegendReviewClips(): Promise<
  LegendDetectionReviewClip[]
> {
  return createLegendDetectionApi().getLegendDetectionReviewClips();
}

/** Every run for a clip, newest first. */
export async function fetchLegendRuns(
  clipId: string,
): Promise<LegendDetectionRun[]> {
  return createLegendDetectionApi().getLegendDetectionRuns({ clipId });
}

export async function fetchLegendUsage(): Promise<LegendDetectionUsage[]> {
  return createLegendDetectionApi().getLegendDetectionUsage();
}

/** Queues another run for a clip; an empty model uses the configured default. */
export async function queueLegendRun(
  clipId: string,
  model: string | null,
): Promise<LegendDetectionRun> {
  return createLegendDetectionApi().queueLegendDetectionRun({
    clipId,
    queueLegendDetectionRunRequest: { provider: null, model },
  });
}

/** Queues the automatic run for every Apex Legends clip that has never had one. */
export async function backfillLegendDetection(): Promise<number> {
  const response = await createLegendDetectionApi().backfillLegendDetection();
  return response.queued;
}

/** Archives every run so the backfill starts from scratch; usage totals keep the archived runs. */
export async function archiveLegendRuns(): Promise<number> {
  const response =
    await createLegendDetectionApi().archiveLegendDetectionRuns();
  return response.archived;
}

export function isRunActive(run: LegendDetectionRun | null | undefined) {
  return run?.status === "pending" || run?.status === "running";
}
