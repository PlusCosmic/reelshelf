import { useQuery } from "@tanstack/react-query";
import { fetchLandingShelf } from "@/shared/services/landingShelf";

/** The popular games the landing page's shelf picks from. The API refreshes them daily. */
export function useLandingShelf() {
  return useQuery({
    queryKey: ["landing-shelf"],
    queryFn: fetchLandingShelf,
    staleTime: 60 * 60_000,
    // A refetch would pick a different dozen; one visit keeps its shelf.
    refetchOnWindowFocus: false,
    retry: false,
  });
}
