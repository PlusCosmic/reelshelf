import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  createRoomConnectionDriver,
  initialRoomConnectionState,
  refreshDelayMs,
  refreshLeadMs,
  startRetryDelaysMs,
  transitionRoomConnection,
  type RoomConnectionEvent,
  type RoomConnectionState,
} from "./roomConnection";

const minute = 60_000;

function run(
  events: RoomConnectionEvent["type"][],
  from: RoomConnectionState = initialRoomConnectionState,
): RoomConnectionState {
  return events.reduce(
    (state, type) =>
      transitionRoomConnection(state, { type } as RoomConnectionEvent),
    from,
  );
}

describe("transitionRoomConnection", () => {
  it("connects, drops, reconnects", () => {
    expect(run(["started"])).toEqual({ kind: "connected" });
    expect(run(["started", "lost"])).toEqual({ kind: "reconnecting" });
    expect(run(["started", "lost", "reconnected"])).toEqual({
      kind: "connected",
    });
  });

  it("retries a failed first connection, then fails", () => {
    expect(run(["startFailed"])).toEqual({ kind: "connecting", attempt: 1 });
    const failures = startRetryDelaysMs.map(() => "startFailed" as const);
    expect(run(failures.slice(0, -1))).toEqual({
      kind: "connecting",
      attempt: startRetryDelaysMs.length - 1,
    });
    expect(run(failures)).toEqual({ kind: "failed" });
    expect(run([...failures, "retry"])).toEqual(initialRoomConnectionState);
  });

  it("starts over when an established connection closes", () => {
    expect(run(["started", "closed"])).toEqual(initialRoomConnectionState);
    expect(run(["started", "lost", "closed"])).toEqual(
      initialRoomConnectionState,
    );
  });

  it("ignores events that mean nothing in a state", () => {
    const connecting = initialRoomConnectionState;
    expect(transitionRoomConnection(connecting, { type: "closed" })).toBe(
      connecting,
    );
    expect(transitionRoomConnection(connecting, { type: "retry" })).toBe(
      connecting,
    );
    const failed: RoomConnectionState = { kind: "failed" };
    expect(transitionRoomConnection(failed, { type: "started" })).toBe(failed);
  });

  it("stays expired whatever happens", () => {
    for (const from of [
      initialRoomConnectionState,
      { kind: "connected" },
      { kind: "reconnecting" },
      { kind: "failed" },
    ] as RoomConnectionState[]) {
      expect(
        run(
          ["tokenExpired", "started", "reconnected", "retry", "closed"],
          from,
        ),
      ).toEqual({ kind: "expired" });
    }
  });
});

describe("refreshDelayMs", () => {
  it("renews ahead of expiry", () => {
    expect(refreshDelayMs(60 * minute)).toBe(60 * minute - refreshLeadMs);
  });

  it("renews soon, but before expiry, when little time is left", () => {
    expect(refreshDelayMs(refreshLeadMs)).toBe(10_000);
    expect(refreshDelayMs(4_000)).toBe(2_000);
    expect(refreshDelayMs(0)).toBe(0);
  });
});

describe("createRoomConnectionDriver", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-11-04T12:00:00Z"));
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  function setup(
    overrides: {
      start?: () => Promise<void>;
      refreshToken?: () => Promise<Date>;
      expiresInMs?: number;
    } = {},
  ) {
    const states: RoomConnectionState["kind"][] = [];
    const start = vi.fn(overrides.start ?? (() => Promise.resolve()));
    const stop = vi.fn(() => Promise.resolve());
    const refreshToken = vi.fn(
      overrides.refreshToken ??
        (() => Promise.resolve(new Date(Date.now() + 60 * minute))),
    );
    const driver = createRoomConnectionDriver({
      start,
      stop,
      refreshToken,
      expiresAt: new Date(Date.now() + (overrides.expiresInMs ?? 60 * minute)),
      onChange: (state) => states.push(state.kind),
    });
    return { driver, start, stop, refreshToken, states };
  }

  it("connects at once", async () => {
    const { driver, start, states } = setup();
    driver.begin();
    await vi.advanceTimersByTimeAsync(0);
    expect(start).toHaveBeenCalledTimes(1);
    expect(states).toEqual(["connected"]);
    driver.dispose();
  });

  it("retries a failed first connection with backoff, then gives up until asked", async () => {
    const { driver, start, states } = setup({
      start: () => Promise.reject(new Error("down")),
    });
    driver.begin();
    await vi.advanceTimersByTimeAsync(0);
    expect(start).toHaveBeenCalledTimes(1);

    await vi.advanceTimersByTimeAsync(startRetryDelaysMs[1] - 1);
    expect(start).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(1);
    expect(start).toHaveBeenCalledTimes(2);

    await vi.advanceTimersByTimeAsync(startRetryDelaysMs[2]);
    expect(start).toHaveBeenCalledTimes(3);
    await vi.advanceTimersByTimeAsync(startRetryDelaysMs[3]);
    expect(start).toHaveBeenCalledTimes(4);
    expect(states.at(-1)).toBe("failed");

    await vi.advanceTimersByTimeAsync(5 * minute);
    expect(start).toHaveBeenCalledTimes(4);

    start.mockImplementation(() => Promise.resolve());
    driver.dispatch({ type: "retry" });
    await vi.advanceTimersByTimeAsync(0);
    expect(start).toHaveBeenCalledTimes(5);
    expect(states.at(-1)).toBe("connected");
    driver.dispose();
  });

  it("connects again after an established connection closes", async () => {
    const { driver, start, states } = setup();
    driver.begin();
    await vi.advanceTimersByTimeAsync(0);
    driver.dispatch({ type: "closed" });
    await vi.advanceTimersByTimeAsync(0);
    expect(start).toHaveBeenCalledTimes(2);
    expect(states).toEqual(["connected", "connecting", "connected"]);
    driver.dispose();
  });

  it("renews the token ahead of expiry", async () => {
    const { driver, refreshToken } = setup();
    driver.begin();
    await vi.advanceTimersByTimeAsync(60 * minute - refreshLeadMs - 1);
    expect(refreshToken).not.toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(1);
    expect(refreshToken).toHaveBeenCalledTimes(1);
    // The renewed token lasts another hour from now.
    await vi.advanceTimersByTimeAsync(60 * minute - refreshLeadMs);
    expect(refreshToken).toHaveBeenCalledTimes(2);
    driver.dispose();
  });

  it("retries a failed renewal soon while the token is valid", async () => {
    const { driver, refreshToken, states } = setup({
      refreshToken: () => Promise.reject(new Error("blip")),
    });
    driver.begin();
    await vi.advanceTimersByTimeAsync(60 * minute - refreshLeadMs);
    expect(refreshToken).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(15_000);
    expect(refreshToken).toHaveBeenCalledTimes(2);

    refreshToken.mockImplementation(() =>
      Promise.resolve(new Date(Date.now() + 60 * minute)),
    );
    await vi.advanceTimersByTimeAsync(15_000);
    expect(refreshToken).toHaveBeenCalledTimes(3);
    expect(states).toEqual(["connected"]);
    driver.dispose();
  });

  it("stops at expiry when renewals keep failing", async () => {
    const { driver, refreshToken, stop, start, states } = setup({
      refreshToken: () => Promise.reject(new Error("down")),
    });
    driver.begin();
    await vi.advanceTimersByTimeAsync(60 * minute - 1);
    expect(states).toEqual(["connected"]);
    const tries = refreshToken.mock.calls.length;
    // Every 15 s over the last five minutes.
    expect(tries).toBe(1 + Math.floor((refreshLeadMs - 1) / 15_000));

    await vi.advanceTimersByTimeAsync(1);
    expect(states).toEqual(["connected", "expired"]);
    expect(stop).toHaveBeenCalledTimes(1);

    // Nothing more is tried, and a reconnect or close afterwards changes nothing.
    driver.dispatch({ type: "closed" });
    await vi.advanceTimersByTimeAsync(60 * minute);
    expect(refreshToken).toHaveBeenCalledTimes(tries);
    expect(start).toHaveBeenCalledTimes(1);
    expect(states).toEqual(["connected", "expired"]);
    driver.dispose();
  });

  it("expires at once when the session's token has already run out", async () => {
    const { driver, refreshToken, states } = setup({ expiresInMs: -1 });
    driver.begin();
    await vi.advanceTimersByTimeAsync(0);
    expect(refreshToken).not.toHaveBeenCalled();
    expect(states).toContain("expired");
    expect(states.at(-1)).toBe("expired");
    driver.dispose();
  });

  it("reports nothing after dispose", async () => {
    const { driver, start, refreshToken, states } = setup();
    driver.begin();
    driver.dispose();
    await vi.advanceTimersByTimeAsync(2 * 60 * minute);
    expect(start).not.toHaveBeenCalled();
    expect(refreshToken).not.toHaveBeenCalled();
    expect(states).toEqual([]);
  });
});
