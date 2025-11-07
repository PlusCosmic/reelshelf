import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { fetchMe, fetchUser } from "@repo/shared/services/user";
import { fetchCategories } from "@repo/shared/services/categories";
import { logout } from "@repo/shared/services/auth";
import {
  addTagToVideo,
  createVideoRequest,
  deleteVideo,
  fetchApexClips,
  fetchUnviewedApexClips,
  getTopTags,
  getVideo,
  markClipAsViewed,
  removeTagFromVideo,
  updateVideoTitle,
} from "@repo/shared/services/apexClips";
import type { Clip, PagedClipsResponse } from "@repo/nucleus-api-client";

// ============================================================================
// Query Hooks
// ============================================================================

/**
 * Fetches the current authenticated user
 * Cached with 5 minute stale time since user data changes infrequently
 */
export function useCurrentUser() {
  return useQuery({
    queryKey: ["user", "me"],
    queryFn: fetchMe,
    staleTime: 5 * 60_000, // 5 minutes
  });
}

/**
 * Fetches a user by their ID
 * Only enabled when userId is provided
 */
export function useUserById(userId: string | undefined | null) {
  return useQuery({
    queryKey: ["user", userId],
    queryFn: () => {
      if (!userId) throw new Error("User ID is required");
      return fetchUser(userId);
    },
    enabled: !!userId,
    staleTime: 5 * 60_000, // 5 minutes
  });
}

/**
 * Fetches all categories
 * Cached with 10 minute stale time since categories rarely change
 */
export function useCategories() {
  return useQuery({
    queryKey: ["categories"],
    queryFn: fetchCategories,
    staleTime: 10 * 60_000, // 10 minutes
  });
}

/**
 * Fetches popular tags
 * Cached with 2 minute stale time for reasonable freshness
 */
export function useTopTags() {
  return useQuery({
    queryKey: ["tags", "top"],
    queryFn: getTopTags,
    staleTime: 2 * 60_000, // 2 minutes
  });
}

/**
 * Parameters for fetching apex clips
 */
export interface ApexClipsParams {
  page: number;
  pageSize: number;
  tags?: string;
  titleSearch?: string;
  unviewedOnly?: boolean;
}

/**
 * Fetches a paginated list of apex clips with optional filters
 * Query key includes all params to ensure proper cache segregation
 */
export function useApexClips(params: ApexClipsParams) {
  const { page, pageSize, tags, titleSearch, unviewedOnly } = params;

  return useQuery({
    queryKey: [
      "clips",
      "apex",
      page,
      pageSize,
      tags,
      titleSearch,
      unviewedOnly,
    ],
    queryFn: async () => {
      if (unviewedOnly) {
        return fetchUnviewedApexClips(page, pageSize, tags, titleSearch);
      }
      return fetchApexClips(page, pageSize, tags, titleSearch);
    },
    staleTime: 30_000, // 30 seconds - balance freshness vs requests
  });
}

/**
 * Fetches a single clip by ID
 * Only enabled when clipId is provided
 */
export function useClip(clipId: string | undefined | null) {
  return useQuery({
    queryKey: ["clips", clipId],
    queryFn: () => {
      if (!clipId) throw new Error("Clip ID is required");
      return getVideo(clipId);
    },
    enabled: !!clipId,
    staleTime: 60_000, // 1 minute
  });
}

// ============================================================================
// Mutation Hooks
// ============================================================================

/**
 * Creates a new video clip request
 * Invalidates all clips queries on success
 */
export function useCreateVideo() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      title,
      md5Hash,
      createdAt,
    }: {
      title: string;
      md5Hash?: string;
      createdAt?: Date;
    }) => createVideoRequest(title, md5Hash, createdAt),
    onSuccess: () => {
      // Invalidate all clips queries to show the new video
      queryClient.invalidateQueries({ queryKey: ["clips"] });
    },
  });
}

/**
 * Updates a clip's title with optimistic updates
 */
export function useUpdateClipTitle() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ clipId, title }: { clipId: string; title: string }) =>
      updateVideoTitle(clipId, title),
    onMutate: async ({ clipId, title }) => {
      // Cancel outgoing refetches
      await queryClient.cancelQueries({ queryKey: ["clips", clipId] });

      // Snapshot the previous value
      const previousClip = queryClient.getQueryData<Clip>(["clips", clipId]);

      // Optimistically update the clip
      if (previousClip) {
        queryClient.setQueryData<Clip>(["clips", clipId], {
          ...previousClip,
          video: {
            ...previousClip.video,
            title,
          },
        });
      }

      return { previousClip };
    },
    onError: (_err, { clipId }, context) => {
      // Rollback on error
      if (context?.previousClip) {
        queryClient.setQueryData(["clips", clipId], context.previousClip);
      }
    },
    onSuccess: (_data, { clipId }) => {
      // Invalidate queries to ensure consistency
      queryClient.invalidateQueries({ queryKey: ["clips", clipId] });
      queryClient.invalidateQueries({ queryKey: ["clips", "apex"] });
    },
  });
}

/**
 * Adds a tag to a clip with optimistic updates
 */
export function useAddTag() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ clipId, tag }: { clipId: string; tag: string }) =>
      addTagToVideo(clipId, tag),
    onMutate: async ({ clipId, tag }) => {
      // Cancel outgoing refetches
      await queryClient.cancelQueries({ queryKey: ["clips", clipId] });

      // Snapshot the previous value
      const previousClip = queryClient.getQueryData<Clip>(["clips", clipId]);

      // Optimistically add the tag
      if (previousClip) {
        queryClient.setQueryData<Clip>(["clips", clipId], {
          ...previousClip,
          tags: [...previousClip.tags, tag],
        });
      }

      return { previousClip };
    },
    onError: (_err, { clipId }, context) => {
      // Rollback on error
      if (context?.previousClip) {
        queryClient.setQueryData(["clips", clipId], context.previousClip);
      }
    },
    onSuccess: (_data, { clipId }) => {
      // Invalidate queries to ensure consistency
      queryClient.invalidateQueries({ queryKey: ["clips", clipId] });
      queryClient.invalidateQueries({ queryKey: ["clips", "apex"] });
      queryClient.invalidateQueries({ queryKey: ["tags", "top"] });
    },
  });
}

/**
 * Removes a tag from a clip with optimistic updates
 */
export function useRemoveTag() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ clipId, tag }: { clipId: string; tag: string }) =>
      removeTagFromVideo(clipId, tag),
    onMutate: async ({ clipId, tag }) => {
      // Cancel outgoing refetches
      await queryClient.cancelQueries({ queryKey: ["clips", clipId] });

      // Snapshot the previous value
      const previousClip = queryClient.getQueryData<Clip>(["clips", clipId]);

      // Optimistically remove the tag
      if (previousClip) {
        queryClient.setQueryData<Clip>(["clips", clipId], {
          ...previousClip,
          tags: previousClip.tags.filter((t) => t !== tag),
        });
      }

      return { previousClip };
    },
    onError: (_err, { clipId }, context) => {
      // Rollback on error
      if (context?.previousClip) {
        queryClient.setQueryData(["clips", clipId], context.previousClip);
      }
    },
    onSuccess: (_data, { clipId }) => {
      // Invalidate queries to ensure consistency
      queryClient.invalidateQueries({ queryKey: ["clips", clipId] });
      queryClient.invalidateQueries({ queryKey: ["clips", "apex"] });
      queryClient.invalidateQueries({ queryKey: ["tags", "top"] });
    },
  });
}

/**
 * Deletes a clip with optimistic updates
 */
export function useDeleteClip() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (clipId: string) => deleteVideo(clipId),
    onMutate: async (clipId) => {
      // Cancel outgoing refetches for all clips queries
      await queryClient.cancelQueries({ queryKey: ["clips"] });

      // Snapshot previous values
      const previousClipsQueries: Array<{
        queryKey: ReadonlyArray<unknown>;
        data: unknown;
      }> = [];

      // Remove clip from all clips list queries optimistically
      queryClient
        .getQueryCache()
        .findAll({ queryKey: ["clips", "apex"] })
        .forEach((query) => {
          const data = query.state.data as PagedClipsResponse | null;
          if (data?.clips) {
            previousClipsQueries.push({
              queryKey: query.queryKey,
              data: data,
            });
            queryClient.setQueryData<PagedClipsResponse>(query.queryKey, {
              ...data,
              clips: data.clips.filter((c) => c.clipId !== clipId),
              totalClips: (data.totalClips || 1) - 1,
            });
          }
        });

      return { previousClipsQueries };
    },
    onError: (_err, _clipId, context) => {
      // Rollback all optimistic updates on error
      if (context?.previousClipsQueries) {
        context.previousClipsQueries.forEach(({ queryKey, data }) => {
          queryClient.setQueryData(queryKey, data);
        });
      }
    },
    onSuccess: (_data, clipId) => {
      // Remove the individual clip from cache
      queryClient.removeQueries({ queryKey: ["clips", clipId] });
      // Invalidate all clips queries to ensure consistency
      queryClient.invalidateQueries({ queryKey: ["clips", "apex"] });
    },
  });
}

/**
 * Marks a clip as viewed
 */
export function useMarkAsViewed() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (clipId: string) => markClipAsViewed(clipId),
    onMutate: async (clipId) => {
      // Cancel outgoing refetches
      await queryClient.cancelQueries({ queryKey: ["clips", clipId] });

      // Snapshot the previous value
      const previousClip = queryClient.getQueryData<Clip>(["clips", clipId]);

      // Optimistically mark as viewed
      if (previousClip) {
        queryClient.setQueryData<Clip>(["clips", clipId], {
          ...previousClip,
          isViewed: true,
        });
      }

      return { previousClip };
    },
    onError: (_err, clipId, context) => {
      // Rollback on error
      if (context?.previousClip) {
        queryClient.setQueryData(["clips", clipId], context.previousClip);
      }
    },
    onSuccess: (_data, clipId) => {
      // Invalidate to ensure consistency
      queryClient.invalidateQueries({ queryKey: ["clips", clipId] });
    },
  });
}

/**
 * Logs out the current user
 * Clears all query cache on success
 */
export function useLogout() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: logout,
    onSuccess: () => {
      // Clear all cached data after logout
      queryClient.clear();
    },
  });
}
