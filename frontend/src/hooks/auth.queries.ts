import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { logout } from "@/shared/services/auth";
import { fetchMe, fetchUser } from "@/shared/services/user";

export function useCurrentUser() {
  return useQuery({
    queryKey: ["user", "me"],
    queryFn: fetchMe,
    retry: false,
    staleTime: 5 * 60_000,
  });
}

export function useUserById(userId: string | undefined | null) {
  return useQuery({
    queryKey: ["user", userId],
    queryFn: () => {
      if (!userId) throw new Error("User ID is required");
      return fetchUser(userId);
    },
    enabled: !!userId,
    staleTime: 5 * 60_000,
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
