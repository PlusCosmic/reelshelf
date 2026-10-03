import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  backfillClipSummary,
  fetchSummaryRuns,
  fetchSummaryUsage,
  isSummaryActive,
  queueSummaryRun,
} from "@/shared/services/clipSummary";

export const clipSummaryQueryKey = ["clip-summary"] as const;
const usageQueryKey = [...clipSummaryQueryKey, "usage"] as const;
const runsQueryKey = (clipId: string) =>
  [...clipSummaryQueryKey, "runs", clipId] as const;

/** Runs are picked up within about ten seconds and take a few more, so poll while any are queued. */
const activePollMs = 4_000;

export function useSummaryRuns(clipId: string | undefined) {
  return useQuery({
    queryKey: runsQueryKey(clipId ?? ""),
    queryFn: () => fetchSummaryRuns(clipId!),
    enabled: !!clipId,
    retry: false,
    refetchInterval: (query) =>
      query.state.data?.some(isSummaryActive) ? activePollMs : false,
  });
}

/** Pass `polling` while runs are queued so the totals keep up with a backfill. */
export function useSummaryUsage(enabled = true, polling = false) {
  return useQuery({
    queryKey: usageQueryKey,
    queryFn: fetchSummaryUsage,
    enabled,
    retry: false,
    refetchInterval: polling ? 10_000 : false,
  });
}

/** Refreshes the usage totals and every clip's summary runs after a run is queued. */
function useInvalidateClipSummary() {
  const queryClient = useQueryClient();
  return () => queryClient.invalidateQueries({ queryKey: clipSummaryQueryKey });
}

export function useQueueSummaryRun() {
  const invalidate = useInvalidateClipSummary();
  return useMutation({
    mutationFn: ({
      clipId,
      provider,
      model,
      reasoningEffort,
    }: {
      clipId: string;
      provider: string | null;
      model: string | null;
      reasoningEffort: string | null;
    }) => queueSummaryRun(clipId, provider, model, reasoningEffort),
    onSuccess: invalidate,
  });
}

export function useBackfillClipSummary() {
  const invalidate = useInvalidateClipSummary();
  return useMutation({
    mutationFn: (reasoningEffort: string | null) =>
      backfillClipSummary(reasoningEffort),
    onSuccess: invalidate,
  });
}
