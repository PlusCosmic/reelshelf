import type {
  DetectedTeammate,
  LegendDetectionLabel,
  LegendDetectionReviewClip,
  LegendDetectionRun,
  LegendDetectionUsage,
} from "@/api-client";
import { createLegendDetectionApi } from "./apiClients";

export type {
  DetectedTeammate,
  LegendDetectionLabel,
  LegendDetectionReviewClip,
  LegendDetectionRun,
  LegendDetectionUsage,
};

/** Every owner's Apex Legends clips, each with its latest detection run. */
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

/** OpenAI's Decisions API: it has its own default model and takes no reasoning effort. */
export const decisionsProvider = "openai-decisions";

/** Which provider, model and reasoning effort to run; null uses the configured default. */
export type LegendRunOptions = {
  provider: string | null;
  model: string | null;
  reasoningEffort: string | null;
};

/** Queues another run for a clip. */
export async function queueLegendRun(
  clipId: string,
  options: LegendRunOptions,
): Promise<LegendDetectionRun> {
  return createLegendDetectionApi().queueLegendDetectionRun({
    clipId,
    queueLegendDetectionRunRequest: options,
  });
}

/** Queues a run for every labelled clip, so the usage table can score it; returns how many were queued. */
export async function queueLabelledLegendRuns(
  options: LegendRunOptions,
): Promise<number> {
  const response =
    await createLegendDetectionApi().queueLegendDetectionLabelledRuns({
      queueLegendDetectionRunRequest: options,
    });
  return response.queued;
}

/** The providers with an API key, which a run can be queued against. */
export async function fetchLegendProviders(): Promise<string[]> {
  return createLegendDetectionApi().getLegendDetectionProviders();
}

/**
 * Queues the automatic run for every Apex Legends clip that has never had one, at the given reasoning effort or
 * the configured default when null.
 */
export async function backfillLegendDetection(
  reasoningEffort: string | null,
): Promise<number> {
  const response = await createLegendDetectionApi().backfillLegendDetection({
    backfillLegendDetectionRequest: { reasoningEffort },
  });
  return response.queued;
}

/** The reasoning efforts a run can ask for; not every model accepts every one. */
export async function fetchLegendReasoningEfforts(): Promise<string[]> {
  return createLegendDetectionApi().getLegendDetectionReasoningEfforts();
}

/** Archives every run so the backfill starts from scratch; usage totals keep the archived runs. */
export async function archiveLegendRuns(): Promise<number> {
  const response =
    await createLegendDetectionApi().archiveLegendDetectionRuns();
  return response.archived;
}

/** The legends on the reference sheet, spelled as labels and results use them. */
export async function fetchLegendNames(): Promise<string[]> {
  return createLegendDetectionApi().getLegendDetectionLegends();
}

/** Records the legends actually in a clip; a null player legend means it can't be identified. */
export async function setLegendLabel(
  clipId: string,
  playerLegend: string | null,
  teammateLegends: string[],
): Promise<void> {
  await createLegendDetectionApi().setLegendDetectionLabel({
    clipId,
    setLegendDetectionLabelRequest: { playerLegend, teammateLegends },
  });
}

export async function deleteLegendLabel(clipId: string): Promise<void> {
  await createLegendDetectionApi().deleteLegendDetectionLabel({ clipId });
}

export function isRunActive(run: LegendDetectionRun | null | undefined) {
  return run?.status === "pending" || run?.status === "running";
}
