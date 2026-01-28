import { HttpError } from "./http";

/**
 * Calls the API to log the current user out.
 * Auth endpoints are at root level (not under /api).
 */
export async function logout(): Promise<void> {
  const url = "/auth/logout";
  const res = await fetch(url, { method: "POST", credentials: "include" });
  if (!res.ok) {
    throw new HttpError(res.status, res.statusText, url);
  }
}
