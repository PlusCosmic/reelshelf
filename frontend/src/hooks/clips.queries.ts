import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { Clip, PagedClipsResponse } from "@/api-client";
import {
  addTagToVideo,
  createVideoRequest,
  deleteVideo,
  fetchClips,
  getSharedClip,
  getTopTags,
  getVideo,
  markClipAsViewed,
  removeTagFromVideo,
  shareVideo,
  updateVideoTitle,
} from "@/shared/services/clips";

export interface ClipsParams {
  categoryId: string;
  page: number;
  pageSize: number;
  tags?: string;
  titleSearch?: string;
  unviewedOnly?: boolean;
  sortOrder?: number;
  startDate?: Date;
  endDate?: Date;
}

export function useTopTags() {
  return useQuery({
    queryKey: ["tags", "top"],
    queryFn: getTopTags,
    staleTime: 2 * 60_000,
  });
}

export function useClips(params: ClipsParams) {
  const {
    categoryId,
    page,
    pageSize,
    tags,
    titleSearch,
    unviewedOnly,
    sortOrder,
    startDate,
    endDate,
  } = params;

  return useQuery({
    queryKey: [
      "clips",
      categoryId,
      page,
      pageSize,
      tags,
      titleSearch,
      unviewedOnly,
      sortOrder,
      startDate?.toISOString(),
      endDate?.toISOString(),
    ],
    queryFn: () =>
      fetchClips({
        categoryId,
        page,
        pageSize,
        tags,
        titleSearch,
        unviewedOnly,
        sortOrder,
        startDate,
        endDate,
      }),
    enabled: !!categoryId,
    staleTime: 30_000,
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

export function useCreateVideo() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      categoryId,
      title,
      md5Hash,
      createdAt,
    }: {
      categoryId: string;
      title: string;
      md5Hash?: string;
      createdAt?: Date;
    }) => createVideoRequest(categoryId, title, md5Hash, createdAt),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["clips"] });
    },
  });
}

export function useUpdateClipTitle() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ clipId, title }: { clipId: string; title: string }) =>
      updateVideoTitle(clipId, title),
    onMutate: async ({ clipId, title }) => {
      await queryClient.cancelQueries({ queryKey: ["clips", clipId] });

      const previousClip = queryClient.getQueryData<Clip>(["clips", clipId]);

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
      if (context?.previousClip) {
        queryClient.setQueryData(["clips", clipId], context.previousClip);
      }
    },
    onSuccess: (_data, { clipId }) => {
      queryClient.invalidateQueries({ queryKey: ["clips", clipId] });
      queryClient.invalidateQueries({ queryKey: ["clips"], exact: false });
    },
  });
}

export function useAddTag() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ clipId, tag }: { clipId: string; tag: string }) =>
      addTagToVideo(clipId, tag),
    onMutate: async ({ clipId, tag }) => {
      await queryClient.cancelQueries({ queryKey: ["clips", clipId] });

      const previousClip = queryClient.getQueryData<Clip>(["clips", clipId]);

      if (previousClip) {
        queryClient.setQueryData<Clip>(["clips", clipId], {
          ...previousClip,
          tags: [...previousClip.tags, tag],
        });
      }

      return { previousClip };
    },
    onError: (_err, { clipId }, context) => {
      if (context?.previousClip) {
        queryClient.setQueryData(["clips", clipId], context.previousClip);
      }
    },
    onSuccess: (_data, { clipId }) => {
      queryClient.invalidateQueries({ queryKey: ["clips", clipId] });
      queryClient.invalidateQueries({ queryKey: ["clips"], exact: false });
      queryClient.invalidateQueries({ queryKey: ["tags", "top"] });
    },
  });
}

export function useRemoveTag() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ clipId, tag }: { clipId: string; tag: string }) =>
      removeTagFromVideo(clipId, tag),
    onMutate: async ({ clipId, tag }) => {
      await queryClient.cancelQueries({ queryKey: ["clips", clipId] });

      const previousClip = queryClient.getQueryData<Clip>(["clips", clipId]);

      if (previousClip) {
        queryClient.setQueryData<Clip>(["clips", clipId], {
          ...previousClip,
          tags: previousClip.tags.filter((item) => item !== tag),
        });
      }

      return { previousClip };
    },
    onError: (_err, { clipId }, context) => {
      if (context?.previousClip) {
        queryClient.setQueryData(["clips", clipId], context.previousClip);
      }
    },
    onSuccess: (_data, { clipId }) => {
      queryClient.invalidateQueries({ queryKey: ["clips", clipId] });
      queryClient.invalidateQueries({ queryKey: ["clips"], exact: false });
      queryClient.invalidateQueries({ queryKey: ["tags", "top"] });
    },
  });
}

export function useDeleteClip() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (clipId: string) => deleteVideo(clipId),
    onMutate: async (clipId) => {
      await queryClient.cancelQueries({ queryKey: ["clips"] });

      const previousClipsQueries: {
        queryKey: ReadonlyArray<unknown>;
        data: unknown;
      }[] = [];

      queryClient
        .getQueryCache()
        .findAll({ queryKey: ["clips"] })
        .forEach((query) => {
          const data = query.state.data as PagedClipsResponse | null;
          if (data?.clips) {
            previousClipsQueries.push({
              queryKey: query.queryKey,
              data,
            });
            queryClient.setQueryData<PagedClipsResponse>(query.queryKey, {
              ...data,
              clips: data.clips.filter((clip) => clip.clipId !== clipId),
              totalClips: Math.max(0, (data.totalClips || 1) - 1),
            });
          }
        });

      return { previousClipsQueries };
    },
    onError: (_err, _clipId, context) => {
      context?.previousClipsQueries.forEach(({ queryKey, data }) => {
        queryClient.setQueryData(queryKey, data);
      });
    },
    onSuccess: (_data, clipId) => {
      queryClient.removeQueries({ queryKey: ["clips", clipId] });
      queryClient.invalidateQueries({ queryKey: ["clips"], exact: false });
    },
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
