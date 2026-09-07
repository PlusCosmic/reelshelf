import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { logout } from "@/shared/services/auth";
import { fetchMe, fetchStorageUsage } from "@/shared/services/user";

export const storageUsageQueryKey = ["user", "me", "storage"] as const;

export function useCurrentUser() {
  return useQuery({
    queryKey: ["user", "me"],
    queryFn: fetchMe,
    retry: false,
    staleTime: 5 * 60_000,
  });
}

export function useStorageUsage(enabled = true) {
  return useQuery({
    queryKey: storageUsageQueryKey,
    queryFn: fetchStorageUsage,
    enabled,
    retry: false,
    staleTime: 30_000,
  });
}

export function useLogout() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: logout,
    onSuccess: () => {
      queryClient.clear();
    },
  });
}
