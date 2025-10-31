import { apiConfig } from "../config/apiConfig";
import { HttpError } from "./http";

/**
 * Calls the API to log the current user out.
 */
export async function logout(): Promise<void> {
  const url = `${apiConfig.baseUrl}/auth/logout`;
  const res = await fetch(url, { method: "POST", credentials: "include" });
  if (!res.ok) {
    throw new HttpError(res.status, res.statusText, url);
  }
}
