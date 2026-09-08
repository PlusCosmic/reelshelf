import {
  useInfiniteQuery,
  useMutation,
  useQuery,
  useQueryClient,
  type QueryClient,
} from "@tanstack/react-query";
import type { Clip } from "@/api-client";
import { storageUsageQueryKey } from "@/hooks/auth.queries";
import {
  deleteClip,
  fetchClips,
  fetchTopTags,
  getSharedClip,
  getVideo,
  markClipAsViewed,
  shareVideo,
} from "@/shared/services/clips";

export const CLIPS_PAGE_SIZE = 48;

/**
 * Drops every cache that describes which clips exist: the paged grids, the shelf totals and topline,
 * and the tag chips. Call it wherever clips are added or removed — all three are computed server-side
 * now, so none of them can be patched up client-side from the mutation's result.
 *
 * Only active queries refetch, so calling this once per clip during a bulk import costs nothing while
 * the user is still on the upload page; the library refetches once, when they navigate back to it.
 */
export function invalidateClipCollections(queryClient: QueryClient) {
  void queryClient.invalidateQueries({ queryKey: ["clips", "library"] });
  void queryClient.invalidateQueries({ queryKey: ["clips", "list"] });
  void queryClient.invalidateQueries({ queryKey: ["clips", "top-tags"] });
}

export type ClipsFilters = {
  categoryId?: string | null;
  tag?: string | null;
  search?: string;
};

/**
 * Pages the caller's clips through the API. Filters go in the query key and on to the server, so
 * searching or filtering covers the whole archive rather than the pages already fetched.
 */
export function useClipsInfinite(filters: ClipsFilters = {}, enabled = true) {
  const search = filters.search?.trim() || undefined;
  const categoryId = filters.categoryId ?? undefined;
  const tag = filters.tag ?? undefined;

  return useInfiniteQuery({
    queryKey: ["clips", "list", { categoryId, tag, search }],
    queryFn: ({ pageParam }) =>
      fetchClips({
        page: pageParam,
        pageSize: CLIPS_PAGE_SIZE,
        categoryId,
        tags: tag ? [tag] : undefined,
        search,
      }),
    initialPageParam: 1,
    getNextPageParam: (lastPage, allPages) =>
      allPages.length < lastPage.totalPages ? allPages.length + 1 : undefined,
    staleTime: 30_000,
    enabled,
  });
}

/**
 * Tag counts across the owner's clips, or one category's. Counted in the database, so the chips do not
 * shrink as you page.
 */
export function useTopTags(categoryId?: string | null, enabled = true) {
  return useQuery({
    queryKey: ["clips", "top-tags", categoryId ?? null],
    queryFn: () => fetchTopTags(categoryId ?? undefined),
    staleTime: 30_000,
    enabled,
  });
}

export function useClip(clipId: string | undefined | null) {
  return useQuery({
    queryKey: ["clips", clipId],
    queryFn: () => {
      if (!clipId) throw new Error("Clip ID is required");
      return getVideo(clipId);
    },
    enabled: !!clipId,
    staleTime: 60_000,
  });
}

export function useMarkAsViewed() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (clipId: string) => markClipAsViewed(clipId),
    onMutate: async (clipId) => {
      await queryClient.cancelQueries({ queryKey: ["clips", clipId] });

      const previousClip = queryClient.getQueryData<Clip>(["clips", clipId]);

      if (previousClip) {
        queryClient.setQueryData<Clip>(["clips", clipId], {
          ...previousClip,
          isViewed: true,
        });
      }

      return { previousClip };
    },
    onError: (_err, clipId, context) => {
      if (context?.previousClip) {
        queryClient.setQueryData(["clips", clipId], context.previousClip);
      }
    },
    onSuccess: (_data, clipId) => {
      queryClient.invalidateQueries({ queryKey: ["clips", clipId] });
    },
  });
}

export function useShareClip() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (clipId: string) => shareVideo(clipId),
    onSuccess: (_data, clipId) => {
      queryClient.invalidateQueries({ queryKey: ["clips", clipId] });
      queryClient.invalidateQueries({ queryKey: ["clips"], exact: false });
    },
  });
}

export function useDeleteClip() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (clipId: string) => deleteClip(clipId),
    onSuccess: (_data, clipId) => {
      queryClient.removeQueries({ queryKey: ["clips", clipId] });
      invalidateClipCollections(queryClient);
      // Playlist summaries and details embed clips; drop the deleted one from them too.
      queryClient.invalidateQueries({ queryKey: ["playlists"] });
      queryClient.invalidateQueries({ queryKey: storageUsageQueryKey });
    },
  });
}

export function useSharedClip(token: string | undefined | null) {
  return useQuery({
    queryKey: ["shared-clips", token],
    queryFn: () => {
      if (!token) throw new Error("Share token is required");
      return getSharedClip(token);
    },
    enabled: !!token,
    staleTime: 60_000,
    retry: false,
  });
}
