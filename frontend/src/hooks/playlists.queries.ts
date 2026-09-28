import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { PlaylistWithDetails } from "@/api-client";
import {
  addClipsToPlaylist,
  addCollaborator,
  createPlaylist,
  deletePlaylist,
  fetchUserSuggestions,
  removeClipFromPlaylist,
  removeCollaborator,
  reorderPlaylistClips,
  updatePlaylist,
} from "@/shared/services/playlists";

/** Every collection query sits under ["playlists"]: the list, and each one's details. */
function useInvalidateCollections() {
  const queryClient = useQueryClient();
  return () => queryClient.invalidateQueries({ queryKey: ["playlists"] });
}

export function useCreateCollection() {
  const invalidate = useInvalidateCollections();
  return useMutation({
    mutationFn: ({
      name,
      description,
    }: {
      name: string;
      description: string | null;
    }) => createPlaylist(name, description),
    onSuccess: invalidate,
  });
}

export function useAddClipToCollection() {
  const invalidate = useInvalidateCollections();
  return useMutation({
    mutationFn: ({
      playlistId,
      clipId,
    }: {
      playlistId: string;
      clipId: string;
    }) => addClipsToPlaylist(playlistId, { clipId }),
    onSuccess: invalidate,
  });
}

export function useRemoveClipFromCollection() {
  const invalidate = useInvalidateCollections();
  return useMutation({
    mutationFn: ({
      playlistId,
      clipId,
    }: {
      playlistId: string;
      clipId: string;
    }) => removeClipFromPlaylist(playlistId, clipId),
    onSuccess: invalidate,
  });
}

export function useUpdateCollection(playlistId: string) {
  const invalidate = useInvalidateCollections();
  return useMutation({
    mutationFn: ({
      name,
      description,
    }: {
      name: string;
      description: string | null;
    }) => updatePlaylist(playlistId, name, description),
    onSuccess: invalidate,
  });
}

export function useDeleteCollection(playlistId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => deletePlaylist(playlistId),
    onSuccess: () => {
      queryClient.removeQueries({ queryKey: ["playlists", playlistId] });
      void queryClient.invalidateQueries({ queryKey: ["playlists"] });
    },
  });
}

/**
 * Saves a new order. The cached collection takes the order at once, so a dragged clip stays where it
 * was dropped, and goes back if the save fails.
 */
export function useReorderCollection(playlistId: string) {
  const queryClient = useQueryClient();
  const key = ["playlists", playlistId];
  return useMutation({
    mutationFn: (clipOrdering: string[]) =>
      reorderPlaylistClips(playlistId, clipOrdering),
    onMutate: async (clipOrdering) => {
      await queryClient.cancelQueries({ queryKey: key });
      const previous = queryClient.getQueryData<PlaylistWithDetails>(key);
      if (previous) {
        const position = new Map(clipOrdering.map((id, index) => [id, index]));
        queryClient.setQueryData<PlaylistWithDetails>(key, {
          ...previous,
          clips: previous.clips.map((item) => ({
            ...item,
            position: position.get(item.clipId) ?? item.position,
          })),
        });
      }
      return { previous };
    },
    onError: (_error, _ordering, context) => {
      if (context?.previous) queryClient.setQueryData(key, context.previous);
    },
    onSettled: () => queryClient.invalidateQueries({ queryKey: ["playlists"] }),
  });
}

export function useInviteToCollection(playlistId: string) {
  const invalidate = useInvalidateCollections();
  return useMutation({
    mutationFn: (person: { userId: string } | { username: string }) =>
      addCollaborator(playlistId, person),
    onSuccess: invalidate,
  });
}

/** Removes someone from a collection, or, with the viewer's own id, leaves it. */
export function useRemovePersonFromCollection(playlistId: string) {
  const invalidate = useInvalidateCollections();
  return useMutation({
    mutationFn: (userId: string) => removeCollaborator(playlistId, userId),
    onSuccess: invalidate,
  });
}

export function useUserSuggestions(enabled: boolean) {
  return useQuery({
    queryKey: ["users", "suggestions"],
    queryFn: fetchUserSuggestions,
    staleTime: 60_000,
    enabled,
  });
}
