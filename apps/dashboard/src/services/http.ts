export type JsonInit = RequestInit & { timeoutMs?: number };

export class HttpError extends Error {
  status: number;
  statusText: string;
  url: string;
  constructor(status: number, statusText: string, url: string) {
    super(`${status} ${statusText} (${url})`);
    this.name = "HttpError";
    this.status = status;
    this.statusText = statusText;
    this.url = url;
  }
}

export async function getJson<T>(url: string, init?: JsonInit): Promise<T> {
  const controller = new AbortController();
  const timeout = init?.timeoutMs ?? 15000;
  const id = setTimeout(() => controller.abort(), timeout);
  try {
    const res = await fetch(url, { ...init, signal: controller.signal });
    if (!res.ok) throw new HttpError(res.status, res.statusText, url);
    return (await res.json()) as T;
  } finally {
    clearTimeout(id);
  }
}

export async function tryGetJson<T>(
  url: string,
  init?: JsonInit,
): Promise<T | null> {
  try {
    return await getJson<T>(url, init);
  } catch (err) {
    if (err instanceof HttpError && (err.status === 404 || err.status === 204))
      return null;
    throw err;
  }
}

export function withQuery(
  base: string,
  params: Record<string, string | number | boolean | undefined | null>,
): string {
  const url = new URL(
    base,
    base.startsWith("http") ? undefined : window.location.origin,
  );
  Object.entries(params).forEach(([k, v]) => {
    if (v === undefined || v === null || v === "") return;
    url.searchParams.set(k, String(v));
  });
  return url.toString();
}
