import {
  keepPreviousData,
  useMutation,
  useQuery,
  useQueryClient,
} from "@tanstack/react-query";
import {
  fetchAccount,
  findAccounts,
  setAccountRole,
  setAccountSuspension,
  setGrantedTier,
  type AccountRole,
  type GrantableTier,
} from "@/shared/services/adminAccounts";

export const adminAccountsQueryKey = ["admin-accounts"] as const;
const searchQueryKey = (query: string) =>
  [...adminAccountsQueryKey, "search", query] as const;
const accountQueryKey = (userId: string) =>
  [...adminAccountsQueryKey, "account", userId] as const;

/** The API answers nothing under two characters, so neither is asked for. */
export function useAccountSearch(query: string) {
  const trimmed = query.trim();
  return useQuery({
    queryKey: searchQueryKey(trimmed),
    queryFn: () => findAccounts(trimmed),
    enabled: trimmed.length >= 2,
    placeholderData: keepPreviousData,
    retry: false,
  });
}

export function useAdminAccount(userId: string | undefined) {
  return useQuery({
    queryKey: accountQueryKey(userId ?? ""),
    queryFn: () => fetchAccount(userId!),
    enabled: !!userId,
    retry: false,
  });
}

/**
 * Changes the role, granted tier and suspension the form changed, one after the other, then refreshes every account
 * view.
 */
export function useSaveAccountAccess() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({
      userId,
      role,
      grantedTier,
      suspended,
      reason,
    }: {
      userId: string;
      role?: AccountRole;
      grantedTier?: GrantableTier | null;
      suspended?: boolean;
      reason: string;
    }) => {
      if (role !== undefined) await setAccountRole(userId, role, reason);
      if (grantedTier !== undefined)
        await setGrantedTier(userId, grantedTier, reason);
      if (suspended !== undefined)
        await setAccountSuspension(userId, suspended, reason);
    },
    onSettled: () =>
      queryClient.invalidateQueries({ queryKey: adminAccountsQueryKey }),
  });
}
