import { describe, expect, it, vi } from "vitest";
import {
  reloadOnStaleChunk,
  staleChunkRetryWindowMs,
  type StaleChunkReloadTarget,
} from "./staleChunkReload";

function fakeTarget(storage: Map<string, string> | "broken" = new Map()) {
  const events = new EventTarget();
  const reload = vi.fn();
  const target: StaleChunkReloadTarget = {
    addEventListener: (type, listener) =>
      events.addEventListener(type, listener),
    location: { reload },
    sessionStorage: {
      getItem: (key) => {
        if (storage === "broken") throw new Error("blocked");
        return storage.get(key) ?? null;
      },
      setItem: (key, value) => {
        if (storage === "broken") throw new Error("blocked");
        storage.set(key, value);
      },
    },
  };
  const fail = () => {
    const event = new Event("vite:preloadError", { cancelable: true });
    events.dispatchEvent(event);
    return event.defaultPrevented;
  };
  return { target, reload, fail };
}

describe("reloadOnStaleChunk", () => {
  it("reloads once when a chunk fails to load", () => {
    const { target, reload, fail } = fakeTarget();
    reloadOnStaleChunk(target, () => 1_000_000);

    expect(fail()).toBe(true);
    expect(reload).toHaveBeenCalledOnce();
  });

  it("lets a second failure soon after the reload surface", () => {
    const storage = new Map<string, string>();
    let now = 1_000_000;
    const first = fakeTarget(storage);
    reloadOnStaleChunk(first.target, () => now);
    first.fail();

    // The reloaded page shares the tab's session storage.
    now += staleChunkRetryWindowMs - 1;
    const second = fakeTarget(storage);
    reloadOnStaleChunk(second.target, () => now);

    expect(second.fail()).toBe(false);
    expect(second.reload).not.toHaveBeenCalled();
  });

  it("reloads again for a later deploy", () => {
    const storage = new Map<string, string>();
    let now = 1_000_000;
    const first = fakeTarget(storage);
    reloadOnStaleChunk(first.target, () => now);
    first.fail();

    now += staleChunkRetryWindowMs;
    const second = fakeTarget(storage);
    reloadOnStaleChunk(second.target, () => now);

    expect(second.fail()).toBe(true);
    expect(second.reload).toHaveBeenCalledOnce();
  });

  it("doesn't reload without session storage to guard against a loop", () => {
    const { target, reload, fail } = fakeTarget("broken");
    reloadOnStaleChunk(target, () => 1_000_000);

    expect(fail()).toBe(false);
    expect(reload).not.toHaveBeenCalled();
  });
});
