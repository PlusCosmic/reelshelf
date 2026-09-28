import { useMutation, useQueryClient } from "@tanstack/react-query";
import {
  addClipsToPlaylist,
  createPlaylist,
  removeClipFromPlaylist,
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
