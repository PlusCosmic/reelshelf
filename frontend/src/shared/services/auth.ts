import { HttpError } from "./http";

export type AuthProvider = "discord" | "twitch";

export const authProviders: ReadonlyArray<{
  id: AuthProvider;
  label: string;
}> = [
  { id: "discord", label: "Discord" },
  { id: "twitch", label: "Twitch" },
];

export function providerLabel(provider: string): string {
  return (
    authProviders.find((candidate) => candidate.id === provider)?.label ??
    provider
  );
}

/**
 * Sends the browser through a provider sign-in. Auth endpoints are at root level (not under /api).
 */
export function startLogin(provider: AuthProvider, returnUrl: string): void {
  window.location.href = `/auth/${provider}/login?returnUrl=${encodeURIComponent(returnUrl)}`;
}

/**
 * Sends the signed-in user through a provider consent screen to attach that identity to their account.
 */
export function startLink(provider: AuthProvider, returnUrl: string): void {
  window.location.href = `/auth/${provider}/link?returnUrl=${encodeURIComponent(returnUrl)}`;
}

/**
 * Calls the API to log the current user out.
 */
export async function logout(): Promise<void> {
  const url = "/auth/logout";
  const res = await fetch(url, { method: "POST", credentials: "include" });
  if (!res.ok) {
    throw new HttpError(res.status, res.statusText, url);
  }
}
