import { useQuery, useQueryClient } from "@tanstack/react-query";
import type { ClipSearchResponse } from "@/api-client";
import {
  browserTimeZone,
  filtersOf,
  type ClipSearchState,
} from "@/components/Reelshelf/clipSearchModel";
import { useFeature } from "@/hooks/featureFlags.queries";
import { createClipSearchApi } from "@/shared/services/apiClients";

const clipSearchKey = (state: ClipSearchState) =>
  ["clips", "search", state] as const;

/**
 * Whether the signed-in account can search by description: the `clip-search` flag is on for it, its plan has
 * transcripts and the API has a model for it. False until both have loaded.
 */
export function useClipSearchAvailable(): boolean {
  const flagged = useFeature("clip-search");
  const availability = useQuery({
    queryKey: ["clips", "search", "availability"],
    queryFn: () => createClipSearchApi().getClipSearchAvailability(),
    enabled: flagged,
    retry: false,
    staleTime: 5 * 60_000,
  });
  return flagged && availability.data?.available === true;
}

/**
 * The clips a search by description finds. A search not yet read by the model is read once; the answer is then filed
 * under the filters it was read into as well, so `onRead` can move the search onto them and every later fetch (a
 * refresh, a filter taken away) skips the model.
 */
export function useClipSearch(
  state: ClipSearchState | null,
  onRead: (next: ClipSearchState) => void,
) {
  const queryClient = useQueryClient();
  return useQuery({
    queryKey: state ? clipSearchKey(state) : ["clips", "search", "none"],
    enabled: state !== null,
    retry: false,
    staleTime: 5 * 60_000,
    queryFn: async (): Promise<ClipSearchResponse> => {
      const current = state!;
      const response = await createClipSearchApi().searchClips({
        clipSearchRequest: {
          query: current.query,
          filters: current.filters,
          timeZone: browserTimeZone(),
        },
      });
      if (!current.filters) {
        const read = {
          query: current.query,
          filters: filtersOf(response.interpretation),
        };
        queryClient.setQueryData(clipSearchKey(read), response);
        onRead(read);
      }
      return response;
    },
  });
}
