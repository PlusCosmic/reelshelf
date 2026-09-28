import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  backfillClipTranscription,
  fetchTranscriptionReviewClips,
  fetchTranscriptionRuns,
  fetchTranscriptionUsage,
  isTranscriptionActive,
  queueTranscriptionRun,
} from "@/shared/services/clipTranscription";

export const clipTranscriptionQueryKey = ["clip-transcription"] as const;
const reviewClipsQueryKey = [...clipTranscriptionQueryKey, "clips"] as const;
const usageQueryKey = [...clipTranscriptionQueryKey, "usage"] as const;
const runsQueryKey = (clipId: string) =>
  [...clipTranscriptionQueryKey, "runs", clipId] as const;

/** Runs are picked up within about ten seconds and take a few more, so poll while any are queued. */
const activePollMs = 4_000;

export function useTranscriptionReviewClips(enabled = true) {
  return useQuery({
    queryKey: reviewClipsQueryKey,
    queryFn: fetchTranscriptionReviewClips,
    enabled,
    retry: false,
    refetchInterval: (query) =>
      query.state.data?.some((clip) => isTranscriptionActive(clip.latestRun))
        ? activePollMs
        : false,
  });
}

export function useTranscriptionRuns(clipId: string | undefined) {
  return useQuery({
    queryKey: runsQueryKey(clipId ?? ""),
    queryFn: () => fetchTranscriptionRuns(clipId!),
    enabled: !!clipId,
    retry: false,
    refetchInterval: (query) =>
      query.state.data?.some(isTranscriptionActive) ? activePollMs : false,
  });
}

/** Pass `polling` while runs are queued so the totals keep up with a backfill. */
export function useTranscriptionUsage(enabled = true, polling = false) {
  return useQuery({
    queryKey: usageQueryKey,
    queryFn: fetchTranscriptionUsage,
    enabled,
    retry: false,
    refetchInterval: polling ? 10_000 : false,
  });
}

/** Refreshes the list, the usage totals and every clip's runs after a run is queued. */
function useInvalidateClipTranscription() {
  const queryClient = useQueryClient();
  return () =>
    queryClient.invalidateQueries({ queryKey: clipTranscriptionQueryKey });
}

export function useQueueTranscriptionRun() {
  const invalidate = useInvalidateClipTranscription();
  return useMutation({
    mutationFn: ({ clipId, model }: { clipId: string; model: string | null }) =>
      queueTranscriptionRun(clipId, model),
    onSuccess: invalidate,
  });
}

export function useBackfillClipTranscription() {
  const invalidate = useInvalidateClipTranscription();
  return useMutation({
    mutationFn: backfillClipTranscription,
    onSuccess: invalidate,
  });
}
