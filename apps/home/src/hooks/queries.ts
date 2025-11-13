import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { fetchMe, logout } from "@repo/shared";
import { loadWeatherConfig } from "../services/config";
import { getWeather } from "../services/weather";
import { getLinks, addLink, deleteLink } from "../services/links";
import { fetchMapRotation } from "../services/apexLegends";
import { getGoogleSuggestions } from "../services/googleSearch";

// ============================================================================
// User Queries
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

// ============================================================================
// Weather Queries
// ============================================================================

export function useWeather() {
  return useQuery({
    queryKey: ["weather"],
    queryFn: async () => {
      const cfg = await loadWeatherConfig();
      const data = await getWeather(cfg);
      return { cfg, data } as const;
    },
    staleTime: 5 * 60_000,
  });
}

// ============================================================================
// Frequent Links Queries
// ============================================================================

/**
 * Fetches frequent links for the current user
 */
export function useLinks() {
  return useQuery({
    queryKey: ["links"],
    queryFn: getLinks,
    staleTime: 2 * 60_000, // 2 minutes
  });
}

/**
 * Adds a new frequent link
 * Optimistically updates the cache and refetches on success
 */
export function useAddLink() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: addLink,
    onSuccess: () => {
      // Refetch links after adding
      queryClient.invalidateQueries({ queryKey: ["links"] });
    },
  });
}

/**
 * Deletes a frequent link
 * Uses optimistic updates for instant UI feedback
 */
export function useDeleteLink() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: deleteLink,
    onMutate: async (linkId: string) => {
      // Cancel any outgoing refetches
      await queryClient.cancelQueries({ queryKey: ["links"] });

      // Snapshot the previous value
      const previousLinks = queryClient.getQueryData(["links"]);

      // Optimistically update to remove the link
      queryClient.setQueryData(["links"], (old: any) =>
        old ? old.filter((link: any) => link.id !== linkId) : []
      );

      // Return a context object with the snapshotted value
      return { previousLinks };
    },
    onError: (_err, _linkId, context) => {
      // If the mutation fails, use the context returned from onMutate to roll back
      if (context?.previousLinks) {
        queryClient.setQueryData(["links"], context.previousLinks);
      }
    },
    onSettled: () => {
      // Always refetch after error or success to ensure consistency
      queryClient.invalidateQueries({ queryKey: ["links"] });
    },
  });
}

// ============================================================================
// Apex Legends Queries
// ============================================================================

/**
 * Fetches Apex Legends map rotation data
 * Refetches every 30 seconds to keep timer accurate
 */
export function useApexMapRotation() {
  return useQuery({
    queryKey: ["apex", "map-rotation"],
    queryFn: fetchMapRotation,
    staleTime: 30_000, // 30 seconds
    refetchInterval: 30_000, // Auto-refetch every 30 seconds
    refetchOnWindowFocus: true, // Refetch when user returns to tab
  });
}

// ============================================================================
// Google Search Queries
// ============================================================================

/**
 * Fetches Google search suggestions for a query
 * Only enabled when query is not empty
 * Short stale time since suggestions change frequently
 */
export function useGoogleSuggestions(query: string | undefined) {
  return useQuery({
    queryKey: ["google-suggestions", query ?? ""],
    queryFn: () => getGoogleSuggestions(query ?? ""),
    enabled: !!query && query.trim().length > 0, // Only fetch if query is not empty
    staleTime: 60_000, // 1 minute
    gcTime: 5 * 60_000, // Keep in cache for 5 minutes
  });
}
