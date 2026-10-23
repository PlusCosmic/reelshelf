import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { logout } from "@/shared/services/auth";
import {
  fetchPlan,
  fetchPlanOffers,
  openBillingPortal,
  startCheckout,
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
