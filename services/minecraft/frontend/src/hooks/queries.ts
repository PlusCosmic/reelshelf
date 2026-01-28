import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { fetchMe } from "@repo/shared/services/user";
import { logout } from "@repo/shared/services/auth";

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
