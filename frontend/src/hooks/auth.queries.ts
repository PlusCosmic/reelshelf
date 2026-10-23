import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { logout } from "@/shared/services/auth";
import {
  fetchExports,
  fetchOverLimit,
  fetchPlan,
  fetchPlanOffers,
  openBillingPortal,
  setClipKept,
  startCheckout,
  startExport,
} from "@/shared/services/plans";
import {
  deleteAccount,
  fetchLinkedIdentities,
  fetchMe,
  setEmail,
  unlinkIdentity,
} from "@/shared/services/user";

export const currentUserQueryKey = ["user", "me"] as const;
export const planQueryKey = ["user", "me", "plan"] as const;
export const planOffersQueryKey = ["plans"] as const;
export const overLimitQueryKey = ["user", "me", "over-limit"] as const;
export const exportsQueryKey = ["user", "me", "exports"] as const;
export const linkedIdentitiesQueryKey = ["user", "me", "identities"] as const;

export function useCurrentUser() {
  return useQuery({
    queryKey: currentUserQueryKey,
    queryFn: fetchMe,
    retry: false,
    staleTime: 5 * 60_000,
  });
}

export function usePlan(enabled = true) {
  return useQuery({
    queryKey: planQueryKey,
    queryFn: fetchPlan,
    enabled,
    retry: false,
    staleTime: 30_000,
  });
}

export function usePlanOffers(enabled = true) {
  return useQuery({
    queryKey: planOffersQueryKey,
    queryFn: fetchPlanOffers,
    enabled,
    retry: false,
    staleTime: 10 * 60_000,
  });
}

export function useOverLimit() {
  return useQuery({
    queryKey: overLimitQueryKey,
    queryFn: fetchOverLimit,
    retry: false,
  });
}

/** Picks or unpicks a clip to keep; the lists and the plan follow. */
export function useSetClipKept() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ clipId, keep }: { clipId: string; keep: boolean }) =>
      setClipKept(clipId, keep),
    onSettled: async () => {
      await queryClient.invalidateQueries({ queryKey: overLimitQueryKey });
    },
  });
}

/** The account's exports; refetched every 5 s while one is being made. */
export function useExports() {
  return useQuery({
    queryKey: exportsQueryKey,
    queryFn: fetchExports,
    retry: false,
    refetchInterval: (query) =>
      query.state.data?.some(
        (e) => e.status === "pending" || e.status === "running",
      )
        ? 5_000
        : false,
  });
}

export function useStartExport() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: startExport,
    onSettled: async () => {
      await queryClient.invalidateQueries({ queryKey: exportsQueryKey });
    },
  });
}

/** Starts Stripe Checkout for a price and sends the browser there. */
export function useStartCheckout() {
  return useMutation({
    mutationFn: startCheckout,
    onSuccess: (url) => {
      window.location.assign(url);
    },
  });
}

/** Opens Stripe's Customer Portal in this tab. */
export function useOpenBillingPortal() {
  return useMutation({
    mutationFn: openBillingPortal,
    onSuccess: (url) => {
      window.location.assign(url);
    },
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
        queryClient.invalidateQueries({ queryKey: planQueryKey }),
      ]);
    },
  });
}

export function useSetEmail() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: setEmail,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: currentUserQueryKey });
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

export function useDeleteAccount() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: deleteAccount,
    onSuccess: () => {
      // The session is gone along with the account, so nothing cached belongs to anyone any more.
      queryClient.clear();
    },
  });
}
