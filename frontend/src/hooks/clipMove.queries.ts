import { useMutation, useQueryClient } from "@tanstack/react-query";
import { invalidateClipCollections } from "@/hooks/clips.queries";
import { moveClips, type MoveTarget } from "@/shared/services/clipMove";

/**
 * Moves clips to another book. Every list, the shelf's counts, tags and legends change with it, and so does each moved
 * clip's own page (its book, and the legend only Apex clips have).
 */
export function useMoveClips() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({
      target,
      categoryId,
    }: {
      target: MoveTarget;
      categoryId: string;
    }) => moveClips(target, categoryId),
    onSettled: (result) => {
      invalidateClipCollections(queryClient);
      void queryClient.invalidateQueries({ queryKey: ["playlists"] });
      for (const { clipId } of result?.moved ?? [])
        void queryClient.invalidateQueries({
          queryKey: ["clips", clipId],
          exact: true,
        });
    },
  });
}
