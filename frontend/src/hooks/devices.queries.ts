import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  type DeviceDecision,
  decideDeviceAuthorization,
  fetchDeviceAuthorization,
  fetchDevices,
  normalizeDeviceCode,
  removeDevice,
} from "@/shared/services/devices";

export const devicesQueryKey = ["user", "me", "devices"] as const;

const deviceAuthorizationQueryKey = (code: string) =>
  ["devices", "authorize", normalizeDeviceCode(code)] as const;

/** The sign-in a code names on /device; null when no live one does. */
export function useDeviceAuthorization(code: string | undefined) {
  return useQuery({
    queryKey: deviceAuthorizationQueryKey(code ?? ""),
    queryFn: () => fetchDeviceAuthorization(code ?? ""),
    enabled: !!code,
    retry: false,
    // A sign-in waits ten minutes at most; asking again on focus would only show a decided one as unknown.
    staleTime: Infinity,
    refetchOnWindowFocus: false,
  });
}

export function useDecideDeviceAuthorization() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({
      code,
      decision,
    }: {
      code: string;
      decision: DeviceDecision;
    }) => decideDeviceAuthorization(code, decision),
    onSuccess: async (_, { decision }) => {
      if (decision === "allow") {
        await queryClient.invalidateQueries({ queryKey: devicesQueryKey });
      }
    },
  });
}

/** The account's devices for Settings → Devices, only fetched while the flag is on. */
export function useDevices(enabled = true) {
  return useQuery({
    queryKey: devicesQueryKey,
    queryFn: fetchDevices,
    enabled,
    retry: false,
    staleTime: 30_000,
  });
}

export function useRemoveDevice() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: removeDevice,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: devicesQueryKey });
    },
  });
}
