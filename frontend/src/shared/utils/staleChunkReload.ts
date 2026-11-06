// A deploy replaces the hashed chunks in /assets, so a tab opened before it fails to load any chunk it hasn't
// fetched yet (a route, the signed-in chrome, the uploader). Vite reports that as `vite:preloadError`; reloading
// picks up the new build. The tab's own marker stops a reload loop when the chunk is missing for another reason:
// a second failure soon after a reload is left to surface as an error.

const storageKey = "reelshelf.stale-chunk-reload-at";
export const staleChunkRetryWindowMs = 10_000;

export interface StaleChunkReloadTarget {
  addEventListener(
    type: "vite:preloadError",
    listener: (event: Event) => void,
  ): void;
  location: { reload(): void };
  sessionStorage: Pick<Storage, "getItem" | "setItem">;
}

export function reloadOnStaleChunk(
  target: StaleChunkReloadTarget = window,
  now: () => number = Date.now,
): void {
  target.addEventListener("vite:preloadError", (event) => {
    const last = readLastReload(target);
    if (last !== null && now() - last < staleChunkRetryWindowMs) return;
    // Without a working session storage there is no loop guard, so don't reload at all.
    if (!writeLastReload(target, now())) return;
    event.preventDefault();
    // An upload in progress keeps its beforeunload warning, so the browser asks before this reload drops it.
    target.location.reload();
  });
}

function readLastReload(target: StaleChunkReloadTarget): number | null {
  try {
    const value = Number(target.sessionStorage.getItem(storageKey));
    return Number.isFinite(value) && value > 0 ? value : null;
  } catch {
    return null;
  }
}

function writeLastReload(target: StaleChunkReloadTarget, at: number): boolean {
  try {
    target.sessionStorage.setItem(storageKey, String(at));
    return true;
  } catch {
    return false;
  }
}
