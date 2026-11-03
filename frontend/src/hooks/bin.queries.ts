import {
  useInfiniteQuery,
  useMutation,
  useQueryClient,
  type InfiniteData,
  type QueryClient,
} from "@tanstack/react-query";
import type { PagedClipsResponse } from "@/api-client";
import { planQueryKey } from "@/hooks/auth.queries";
import { invalidateClipCollections } from "@/hooks/clips.queries";
import {
  binClips,
  deleteBinnedClips,
  fetchClipBin,
  restoreClips,
  type BinTarget,
} from "@/shared/services/bin";

export const binQueryKey = ["clips", "bin"] as const;
const BIN_PAGE_SIZE = 48;

/** The caller's bin, newest in first. */
export function useClipBin() {
  return useInfiniteQuery({
    queryKey: binQueryKey,
    queryFn: ({ pageParam }) => fetchClipBin(pageParam, BIN_PAGE_SIZE),
    initialPageParam: 1,
    getNextPageParam: (lastPage, allPages) =>
      allPages.length < lastPage.totalPages ? allPages.length + 1 : undefined,
  });
}

/**
 * Takes clips out of every cached clip list at once, so the rows go the moment they are binned rather than when the
 * lists come back from the server.
 */
export function dropClipsFromCachedLists(
  queryClient: QueryClient,
  clipIds: Iterable<string>,
) {
  const gone = new Set(clipIds);
  queryClient.setQueriesData<InfiniteData<PagedClipsResponse>>(
    { queryKey: ["clips", "list"] },
    (data) =>
      data?.pages
        ? {
            ...data,
            pages: data.pages.map((page) => {
              const clips = page.clips.filter((clip) => !gone.has(clip.clipId));
              return {
                ...page,
                clips,
                totalClips:
                  page.totalClips - (page.clips.length - clips.length),
              };
            }),
          }
        : data,
  );
}

/** Everything that changes when clips go in or out of the bin, or leave it for good. */
function refreshAfterBinChange(
  queryClient: QueryClient,
  clipIds: Iterable<string>,
) {
  invalidateClipCollections(queryClient);
  void queryClient.invalidateQueries({ queryKey: binQueryKey });
  // Collections leave binned clips out, and a clip's page says whether it is in the bin.
  void queryClient.invalidateQueries({ queryKey: ["playlists"] });
  void queryClient.invalidateQueries({ queryKey: planQueryKey });
  for (const clipId of clipIds) {
    void queryClient.invalidateQueries({
      queryKey: ["clips", clipId],
      exact: true,
    });
  }
}

export function useBinClips() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (target: BinTarget) => binClips(target),
    onMutate: (target) => {
      if ("clipIds" in target)
        dropClipsFromCachedLists(queryClient, target.clipIds);
    },
    onSuccess: (result) => {
      dropClipsFromCachedLists(queryClient, result.clipIds);
      refreshAfterBinChange(queryClient, result.clipIds);
    },
    // A failed bin puts the rows back.
    onError: () => invalidateClipCollections(queryClient),
  });
}

export function useRestoreClips() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (clipIds: string[]) => restoreClips(clipIds),
    onSuccess: (result) => refreshAfterBinChange(queryClient, result.clipIds),
  });
}

export function useDeleteBinnedClips() {
  const queryClient = useQueryClient();
  return useMutation({
    /**
     * These clips, or (with null) the whole bin, a part at a time until nothing more can go: a clip held back stays,
     * and the bin stops there rather than asking for it again.
     */
    mutationFn: async (clipIds: string[] | null) => {
      const deleted: string[] = [];
      const held: string[] = [];
      for (;;) {
        const result = await deleteBinnedClips(clipIds);
        deleted.push(...result.deletedIds);
        held.push(...result.heldIds);
        if (clipIds || result.remaining === 0 || result.deletedIds.length === 0)
          return { deleted, held, remaining: result.remaining };
      }
    },
    onSettled: () => refreshAfterBinChange(queryClient, []),
  });
}
