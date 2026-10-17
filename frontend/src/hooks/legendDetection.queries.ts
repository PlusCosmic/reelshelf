import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  archiveLegendRuns,
  clearLegendLabels,
  fetchLegendBaseline,
  setLegendPlayerLabels,
  type LegendBaselineClip,
  type PlayerLabel,
  backfillLegendDetection,
  deleteLegendLabel,
  fetchLegendNames,
  fetchLegendProviders,
  fetchLegendReasoningEfforts,
  setLegendLabel,
  fetchLegendReviewClips,
  fetchLegendRuns,
  fetchLegendUsage,
  isRunActive,
  createLegendEval,
  deleteLegendEval,
  fetchLegendCascade,
  fetchLegendEvalMisses,
  fetchLegendEvals,
  fetchLegendModelPrices,
  setLegendModelPrice,
  queueLegendRun,
  type LegendRunOptions,
} from "@/shared/services/legendDetection";

export const legendDetectionQueryKey = ["legend-detection"] as const;
const reviewClipsQueryKey = [...legendDetectionQueryKey, "clips"] as const;
const usageQueryKey = [...legendDetectionQueryKey, "usage"] as const;
const baselineQueryKey = [...legendDetectionQueryKey, "baseline"] as const;
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
      ...options
    }: LegendRunOptions & { clipId: string }) =>
      queueLegendRun(clipId, options),
    onSuccess: invalidate,
  });
}

const evalsQueryKey = [...legendDetectionQueryKey, "evals"] as const;

/** Polls while any eval still has queued runs, so its row fills in as runs finish. */
export function useLegendEvals(enabled = true) {
  return useQuery({
    queryKey: evalsQueryKey,
    queryFn: fetchLegendEvals,
    enabled,
    retry: false,
    refetchInterval: (query) =>
      query.state.data?.some((evaluation) => evaluation.queued > 0)
        ? activePollMs
        : false,
  });
}

export function useLegendCascade(
  first: string | undefined,
  escalation: string | undefined,
) {
  return useQuery({
    queryKey: [...evalsQueryKey, "cascade", first ?? "", escalation ?? ""],
    queryFn: () => fetchLegendCascade(first!, escalation!),
    enabled: !!first && !!escalation && first !== escalation,
    retry: false,
  });
}

export function useLegendEvalMisses(evalId: string | undefined) {
  return useQuery({
    queryKey: [...evalsQueryKey, "misses", evalId ?? ""],
    queryFn: () => fetchLegendEvalMisses(evalId!),
    enabled: !!evalId,
    retry: false,
  });
}

export function useCreateLegendEval() {
  const invalidate = useInvalidateLegendDetection();
  return useMutation({ mutationFn: createLegendEval, onSuccess: invalidate });
}

export function useDeleteLegendEval() {
  const invalidate = useInvalidateLegendDetection();
  return useMutation({ mutationFn: deleteLegendEval, onSuccess: invalidate });
}

export function useLegendModelPrices(enabled = true) {
  return useQuery({
    queryKey: [...legendDetectionQueryKey, "prices"],
    queryFn: fetchLegendModelPrices,
    enabled,
    retry: false,
  });
}

/** Saving a price re-costs every eval, so both are refreshed. */
export function useSetLegendModelPrice() {
  const invalidate = useInvalidateLegendDetection();
  return useMutation({
    mutationFn: setLegendModelPrice,
    onSuccess: invalidate,
  });
}

export function useLegendBaseline(enabled = true) {
  return useQuery({
    queryKey: baselineQueryKey,
    queryFn: fetchLegendBaseline,
    enabled,
    retry: false,
  });
}

/**
 * A change to many clips' labels on the baseline page: labels to set, and clips whose label to remove. Applied to
 * the cached baseline at once, so confirming a group never waits on the network, and rolled back if it fails.
 */
export type BaselineLabelChange = {
  set: PlayerLabel[];
  clear: string[];
};

export function useChangeBaselineLabels() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ set, clear }: BaselineLabelChange) => {
      if (set.length > 0) await setLegendPlayerLabels(set);
      if (clear.length > 0) await clearLegendLabels(clear);
    },
    onMutate: async ({ set, clear }: BaselineLabelChange) => {
      await queryClient.cancelQueries({ queryKey: baselineQueryKey });
      const previous =
        queryClient.getQueryData<LegendBaselineClip[]>(baselineQueryKey);
      const labelledAt = new Date();
      const setById = new Map(set.map((label) => [label.clipId, label]));
      const cleared = new Set(clear);
      queryClient.setQueryData<LegendBaselineClip[]>(
        baselineQueryKey,
        (clips) =>
          clips?.map((clip) => {
            const label = setById.get(clip.clipId);
            if (label)
              return {
                ...clip,
                label: { playerLegend: label.playerLegend, labelledAt },
              };
            return cleared.has(clip.clipId) ? { ...clip, label: null } : clip;
          }),
      );
      return { previous };
    },
    onError: (_error, _change, context) => {
      if (context?.previous)
        queryClient.setQueryData(baselineQueryKey, context.previous);
    },
    // The review page and usage table score against labels; the baseline itself is already up to date.
    onSettled: () =>
      queryClient.invalidateQueries({
        queryKey: legendDetectionQueryKey,
        predicate: (query) => query.queryKey[1] !== "baseline",
      }),
  });
}

export function useLegendProviders(enabled = true) {
  return useQuery({
    queryKey: [...legendDetectionQueryKey, "providers"],
    queryFn: fetchLegendProviders,
    enabled,
    retry: false,
    staleTime: Infinity,
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
