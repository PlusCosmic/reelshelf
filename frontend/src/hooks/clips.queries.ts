import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { Clip } from "@/api-client";
import {
  getSharedClip,
  getVideo,
  markClipAsViewed,
  shareVideo,
} from "@/shared/services/clips";

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
