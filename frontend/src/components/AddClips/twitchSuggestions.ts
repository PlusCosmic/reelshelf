import { useCallback, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { TwitchClipSummary } from "@/api-client";
import { invalidateClipCollections } from "@/hooks/clips.queries";
import { fetchTwitchClips, importTwitchClip } from "@/shared/services/twitch";
import { sortTwitchClips } from "./addClipsModel";

const dismissedKey = "reelshelf-dismissed-twitch-games";
export const twitchSuggestionsKey = ["twitch", "suggestions"] as const;

function readDismissed(): number[] {
  try {
    const stored = JSON.parse(localStorage.getItem(dismissedKey) ?? "[]");
    return Array.isArray(stored) ? stored.filter(Number.isInteger) : [];
  } catch {
    return [];
  }
}

/**
 * The owner's Twitch clips that aren't on the shelf yet, sorted into the books they belong in and
 * the games that could become books. Reads the most recent page of clips (up to 100) only; it is a
 * nudge, and the full Twitch list is still there to page through.
 */
export function useTwitchSuggestions() {
  const [dismissed, setDismissed] = useState(readDismissed);
  const query = useQuery({
    queryKey: twitchSuggestionsKey,
    queryFn: () => fetchTwitchClips({ first: 100 }),
    staleTime: 10 * 60_000,
  });

  const suggestions = useMemo(
    () => sortTwitchClips(query.data?.clips ?? [], new Set(dismissed)),
    [dismissed, query.data],
  );

  const dismissGame = useCallback((igdbId: number) => {
    setDismissed((current) => {
      const next = [...new Set([...current, igdbId])];
      try {
        localStorage.setItem(dismissedKey, JSON.stringify(next));
      } catch {
        // Private windows and blocked storage: the ghost just comes back next visit.
      }
      return next;
    });
  }, []);

  return {
    linked: query.data?.state === "Ready",
    ...suggestions,
    dismissGame,
  };
}

/** Imports Twitch clips into one book, one at a time, counting as it goes. */
export function useImportTwitchClips(categoryId: string) {
  const queryClient = useQueryClient();
  const [done, setDone] = useState(0);
  const mutation = useMutation({
    mutationFn: async (clips: TwitchClipSummary[]) => {
      setDone(0);
      for (const clip of clips) {
        try {
          await importTwitchClip({ clipId: clip.id, categoryId });
        } catch {
          // One clip failing (gone from Twitch, storage full) shouldn't stop the rest.
        }
        setDone((count) => count + 1);
        invalidateClipCollections(queryClient);
      }
    },
    onSettled: () =>
      queryClient.invalidateQueries({ queryKey: twitchSuggestionsKey }),
  });
  return { importClips: mutation.mutate, importing: mutation.isPending, done };
}
