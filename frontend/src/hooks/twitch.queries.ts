import { useInfiniteQuery } from "@tanstack/react-query";
import {
  fetchTwitchClips,
  type TwitchClipsWindow,
} from "@/shared/services/twitch";

export const twitchClipsQueryKey = ["twitch", "clips"] as const;

/** Pages of the signed-in user's Twitch clips; the first page also reports whether Twitch is linked and authorized. */
export function useTwitchClips(days: TwitchClipsWindow, enabled = true) {
  return useInfiniteQuery({
    queryKey: [...twitchClipsQueryKey, days ?? "all"],
    queryFn: ({ pageParam }) =>
      fetchTwitchClips({ cursor: pageParam || null, days }),
    initialPageParam: "",
    getNextPageParam: (lastPage) =>
      lastPage.state === "Ready" && lastPage.cursor
        ? lastPage.cursor
        : undefined,
    enabled,
    retry: false,
    staleTime: 60_000,
  });
}
