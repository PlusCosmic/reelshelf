import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  archiveLegendRuns,
  backfillLegendDetection,
  deleteLegendLabel,
  fetchLegendNames,
  fetchLegendReasoningEfforts,
  setLegendLabel,
  fetchLegendReviewClips,
  fetchLegendRuns,
  fetchLegendUsage,
  isRunActive,
  queueLegendRun,
} from "@/shared/services/legendDetection";

export const legendDetectionQueryKey = ["legend-detection"] as const;
const reviewClipsQueryKey = [...legendDetectionQueryKey, "clips"] as const;
const usageQueryKey = [...legendDetectionQueryKey, "usage"] as const;
const runsQueryKey = (clipId: string) =>
  [...legendDetectionQueryKey, "runs", clipId] as const;

/** Runs are picked up within about ten seconds and take a few more, so poll while any are queued. */
const activePollMs = 4_000;

export function useLegendReviewClips(enabled = true) {
  return useQuery({
    queryKey: reviewClipsQueryKey,
    queryFn: fetchLegendReviewClips,
    enabled,
    retry: false,
    refetchInterval: (query) =>
      query.state.data?.some((clip) => isRunActive(clip.latestRun))
        ? activePollMs
        : false,
  });
}

export function useLegendRuns(clipId: string | undefined) {
  return useQuery({
    queryKey: runsQueryKey(clipId ?? ""),
    queryFn: () => fetchLegendRuns(clipId!),
    enabled: !!clipId,
    retry: false,
    refetchInterval: (query) =>
      query.state.data?.some(isRunActive) ? activePollMs : false,
  });
}

/** Pass `polling` while runs are queued so the totals keep up with a backfill. */
export function useLegendUsage(enabled = true, polling = false) {
  return useQuery({
    queryKey: usageQueryKey,
    queryFn: fetchLegendUsage,
    enabled,
    retry: false,
    refetchInterval: polling ? 10_000 : false,
  });
}

/** Refreshes the list, the usage totals and every clip's runs after a run is queued. */
function useInvalidateLegendDetection() {
  const queryClient = useQueryClient();
  return () =>
    queryClient.invalidateQueries({ queryKey: legendDetectionQueryKey });
}

export function useQueueLegendRun() {
  const invalidate = useInvalidateLegendDetection();
  return useMutation({
    mutationFn: ({
      clipId,
      model,
      reasoningEffort,
    }: {
      clipId: string;
      model: string | null;
      reasoningEffort: string | null;
    }) => queueLegendRun(clipId, model, reasoningEffort),
    onSuccess: invalidate,
  });
}

export function useLegendNames(enabled = true) {
  return useQuery({
    queryKey: [...legendDetectionQueryKey, "legends"],
    queryFn: fetchLegendNames,
    enabled,
    retry: false,
    staleTime: Infinity,
  });
}

export function useLegendReasoningEfforts(enabled = true) {
  return useQuery({
    queryKey: [...legendDetectionQueryKey, "reasoning-efforts"],
    queryFn: fetchLegendReasoningEfforts,
    enabled,
    retry: false,
    staleTime: Infinity,
  });
}

export function useSetLegendLabel() {
  const invalidate = useInvalidateLegendDetection();
  return useMutation({
    mutationFn: ({
      clipId,
      playerLegend,
      teammateLegends,
    }: {
      clipId: string;
      playerLegend: string | null;
      teammateLegends: string[];
    }) => setLegendLabel(clipId, playerLegend, teammateLegends),
    onSuccess: invalidate,
  });
}

export function useDeleteLegendLabel() {
  const invalidate = useInvalidateLegendDetection();
  return useMutation({
    mutationFn: deleteLegendLabel,
    onSuccess: invalidate,
  });
}

export function useArchiveLegendRuns() {
  const invalidate = useInvalidateLegendDetection();
  return useMutation({
    mutationFn: archiveLegendRuns,
    onSuccess: invalidate,
  });
}

export function useBackfillLegendDetection() {
  const invalidate = useInvalidateLegendDetection();
  return useMutation({
    mutationFn: backfillLegendDetection,
    onSuccess: invalidate,
  });
}
