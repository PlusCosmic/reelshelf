import type { DeviceAuthorizationRequest, DeviceSummary } from "@/api-client";
import { createDeviceApi } from "./apiClients";
import { ApiError } from "./apiError";

export type { DeviceAuthorizationRequest, DeviceSummary };

/** A code as people type it, matched as the API matches it: ignoring case, spaces and the dash. */
export function normalizeDeviceCode(typed: string): string {
  return typed.replace(/[\s-]/g, "").toUpperCase();
}

/** "KDRWTPLM" as people see it, "KDRW-TPLM". */
export function formatDeviceCode(code: string): string {
  const normalized = normalizeDeviceCode(code);
  return normalized.length === 8
    ? `${normalized.slice(0, 4)}-${normalized.slice(4)}`
    : normalized;
}

/** The sign-in a code names while it waits for an answer, or null when no live one does (404). */
export async function fetchDeviceAuthorization(
  code: string,
): Promise<DeviceAuthorizationRequest | null> {
  try {
    return await createDeviceApi().getDeviceAuthorization({
      userCode: normalizeDeviceCode(code),
    });
  } catch (error) {
    if (error instanceof ApiError && error.status === 404) return null;
    throw error;
  }
}

export type DeviceDecision = "allow" | "deny";

/**
 * Answers a sign-in. Throws an `ApiError`: 404 when the code no longer names a live request, 409 when it was answered
 * already.
 */
export async function decideDeviceAuthorization(
  code: string,
  decision: DeviceDecision,
): Promise<void> {
  const userCode = normalizeDeviceCode(code);
  if (decision === "allow") {
    await createDeviceApi().approveDevice({ userCode });
  } else {
    await createDeviceApi().denyDevice({ userCode });
  }
}

export async function fetchDevices(): Promise<DeviceSummary[]> {
  return createDeviceApi().getMyDevices();
}

/** Signs the device out at once: its token stops working on the next request. */
export async function removeDevice(deviceId: string): Promise<void> {
  await createDeviceApi().removeMyDevice({ deviceId });
}

/**
 * Whether to offer the Windows uploader: a Windows browser on a device that isn't touch-first (a Surface in tablet
 * mode, or a phone asking for the desktop site, gets nothing).
 */
export function isWindowsDesktop(
  nav: Navigator | undefined = typeof navigator === "undefined"
    ? undefined
    : navigator,
  coarsePointer: boolean = typeof window !== "undefined" &&
    typeof window.matchMedia === "function" &&
    window.matchMedia("(pointer: coarse)").matches,
): boolean {
  if (!nav) return false;
  const platform =
    (nav as Navigator & { userAgentData?: { platform?: string } }).userAgentData
      ?.platform ||
    nav.platform ||
    "";
  return /^win/i.test(platform) && !coarsePointer;
}
