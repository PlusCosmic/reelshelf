import type { ClipSummaryRun, ClipSummaryUsage } from "@/api-client";
import { createClipSummaryApi } from "./apiClients";

export type { ClipSummaryRun, ClipSummaryUsage };

/** Every summary run for a clip, newest first. */
export async function fetchSummaryRuns(
  clipId: string,
): Promise<ClipSummaryRun[]> {
  return createClipSummaryApi().getClipSummaryRuns({ clipId });
}

export async function fetchSummaryUsage(): Promise<ClipSummaryUsage[]> {
  return createClipSummaryApi().getClipSummaryUsage();
}

/** Queues another summary of the clip's latest transcript; null settings use the configured defaults. */
export async function queueSummaryRun(
  clipId: string,
  provider: string | null,
  model: string | null,
  reasoningEffort: string | null,
): Promise<ClipSummaryRun> {
  return createClipSummaryApi().queueClipSummaryRun({
    clipId,
    queueClipSummaryRunRequest: { provider, model, reasoningEffort },
  });
}

/** Queues the automatic summary for every transcribed clip that has never had one. */
export async function backfillClipSummary(
  reasoningEffort: string | null,
): Promise<number> {
  const response = await createClipSummaryApi().backfillClipSummary({
    backfillClipSummaryRequest: { reasoningEffort },
  });
  return response.queued;
}

export function isSummaryActive(run: ClipSummaryRun | null | undefined) {
  return run?.status === "pending" || run?.status === "running";
}
