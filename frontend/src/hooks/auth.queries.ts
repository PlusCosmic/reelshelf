import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { logout } from "@/shared/services/auth";
import {
  fetchLinkedIdentities,
  fetchMe,
  fetchStorageUsage,
  unlinkIdentity,
} from "@/shared/services/user";

export const currentUserQueryKey = ["user", "me"] as const;
export const storageUsageQueryKey = ["user", "me", "storage"] as const;
export const linkedIdentitiesQueryKey = ["user", "me", "identities"] as const;

export function useCurrentUser() {
  return useQuery({
    queryKey: currentUserQueryKey,
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

export function useLinkedIdentities(enabled = true) {
  return useQuery({
    queryKey: linkedIdentitiesQueryKey,
    queryFn: fetchLinkedIdentities,
    enabled,
    retry: false,
    staleTime: 30_000,
  });
}

export function useUnlinkIdentity() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: unlinkIdentity,
    onSuccess: async () => {
      // Unlinking the primary identity changes the account's name and avatar too.
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: linkedIdentitiesQueryKey }),
        queryClient.invalidateQueries({ queryKey: currentUserQueryKey }),
        queryClient.invalidateQueries({ queryKey: storageUsageQueryKey }),
      ]);
    },
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
