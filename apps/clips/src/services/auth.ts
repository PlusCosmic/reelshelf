import { apiConfig } from '../config/apiConfig.ts'

/**
 * Calls the API to log the current user out.
 */
export async function logout(): Promise<void> {
  const url = `${apiConfig.baseUrl}/auth/logout`
  await fetch(url, { method: 'POST', credentials: 'include' })
}
