import type {
  ClipTranscriptionReviewClip,
  ClipTranscriptionRun,
  ClipTranscriptionUsage,
} from "@/api-client";
import { createClipTranscriptionApi } from "./apiClients";

export type {
  ClipTranscriptionReviewClip,
  ClipTranscriptionRun,
  ClipTranscriptionUsage,
};

/** Whitelisted owners' clips and any other transcribed clip, each with its latest run. */
export async function fetchTranscriptionReviewClips(): Promise<
  ClipTranscriptionReviewClip[]
> {
  return createClipTranscriptionApi().getClipTranscriptionReviewClips();
}

/** Every run for a clip, newest first. */
export async function fetchTranscriptionRuns(
  clipId: string,
): Promise<ClipTranscriptionRun[]> {
  return createClipTranscriptionApi().getClipTranscriptionRuns({ clipId });
}

export async function fetchTranscriptionUsage(): Promise<
  ClipTranscriptionUsage[]
> {
  return createClipTranscriptionApi().getClipTranscriptionUsage();
}

/** Queues another run for a clip; a null model uses the configured default. */
export async function queueTranscriptionRun(
  clipId: string,
  model: string | null,
): Promise<ClipTranscriptionRun> {
  return createClipTranscriptionApi().queueClipTranscriptionRun({
    clipId,
    queueClipTranscriptionRunRequest: { model },
  });
}

/** Queues the automatic run for every encoded clip of a whitelisted owner that has never had one. */
export async function backfillClipTranscription(): Promise<number> {
  const response =
    await createClipTranscriptionApi().backfillClipTranscription();
  return response.queued;
}

/**
 * Re-runs, with the configured model, every clip whose latest run found audio but no words using another model.
 */
export async function retryEmptyTranscriptions(): Promise<number> {
  const response =
    await createClipTranscriptionApi().retryEmptyClipTranscriptions();
  return response.queued;
}

export function isTranscriptionActive(
  run: ClipTranscriptionRun | null | undefined,
) {
  return run?.status === "pending" || run?.status === "running";
}
