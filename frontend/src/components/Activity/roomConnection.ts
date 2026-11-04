/**
 * The watch room connection's lifecycle, kept apart from React and SignalR so it can be tested on its own.
 *
 * - `connecting`: the first connection (or one after the hub closed) is being tried, `attempt` counting from 0.
 *   SignalR's automatic reconnect never covers a failed first `start()`, so this retries it after
 *   `startRetryDelaysMs[attempt]` and gives up into `failed` once those run out.
 * - `connected`: the room is live; only now do room controls do anything.
 * - `reconnecting`: an established connection dropped and SignalR is bringing it back by itself.
 * - `failed`: the first connection never came up; the viewer can try again.
 * - `expired`: the room token ran out without being renewed. Every reconnect would be refused, so the
 *   connection is stopped and nothing more is tried; the viewer reopens the Activity.
 */
export type RoomConnectionState =
  | { kind: "connecting"; attempt: number }
  | { kind: "connected" }
  | { kind: "reconnecting" }
  | { kind: "failed" }
  | { kind: "expired" };

export type RoomConnectionStatus = RoomConnectionState["kind"];

export type RoomConnectionEvent =
  /** `start()` resolved. */
  | { type: "started" }
  /** `start()` rejected. */
  | { type: "startFailed" }
  /** SignalR began reconnecting an established connection. */
  | { type: "lost" }
  /** SignalR's reconnect succeeded. */
  | { type: "reconnected" }
  /** The connection closed for good without being asked to. */
  | { type: "closed" }
  /** The viewer asked to try again after `failed`. */
  | { type: "retry" }
  /** The room token expired without being renewed. */
  | { type: "tokenExpired" };

/** Waits before each try of the first connection, as SignalR's own reconnect does; then `failed`. */
export const startRetryDelaysMs = [0, 2_000, 10_000, 30_000] as const;

/** Renew the room token this long before it expires, so a reconnect never presents an expired one. */
export const refreshLeadMs = 5 * 60_000;

/** After a failed renewal, try again this soon (or at expiry, if that comes first). */
export const refreshRetryMs = 15_000;

export const initialRoomConnectionState: RoomConnectionState = {
  kind: "connecting",
  attempt: 0,
};

/** The next state, or the same object when the event changes nothing there. */
export function transitionRoomConnection(
  state: RoomConnectionState,
  event: RoomConnectionEvent,
): RoomConnectionState {
  if (state.kind === "expired") return state;
  if (event.type === "tokenExpired") return { kind: "expired" };

  switch (state.kind) {
    case "connecting":
      if (event.type === "started") return { kind: "connected" };
      if (event.type === "startFailed") {
        const attempt = state.attempt + 1;
        return attempt < startRetryDelaysMs.length
          ? { kind: "connecting", attempt }
          : { kind: "failed" };
      }
      return state;
    case "connected":
    case "reconnecting":
      if (event.type === "lost") return { kind: "reconnecting" };
      if (event.type === "reconnected") return { kind: "connected" };
      if (event.type === "closed") return initialRoomConnectionState;
      return state;
    case "failed":
      return event.type === "retry" ? initialRoomConnectionState : state;
  }
}

/** How long to wait before renewing a token that expires in `remainingMs`. */
export function refreshDelayMs(remainingMs: number): number {
  if (remainingMs <= 0) return 0;
  return Math.max(
    Math.min(10_000, remainingMs / 2),
    remainingMs - refreshLeadMs,
  );
}

export type RoomConnectionDriverOptions = {
  start: () => Promise<void>;
  stop: () => Promise<void>;
  /** Renews the room token (keeping the new one) and answers when it expires. */
  refreshToken: () => Promise<Date>;
  expiresAt: Date;
  onChange: (state: RoomConnectionState) => void;
  now?: () => number;
};

export type RoomConnectionDriver = {
  /** Starts connecting and renewing the token. */
  begin: () => void;
  dispatch: (event: RoomConnectionEvent) => void;
  /** Stops everything; nothing is reported afterwards. */
  dispose: () => void;
};

/**
 * Runs the transitions: (re)tries `start()` while `connecting`, renews the token before it expires (and soon
 * again after a failed renewal), and stops the connection once the token has expired.
 */
export function createRoomConnectionDriver(
  options: RoomConnectionDriverOptions,
): RoomConnectionDriver {
  const now = options.now ?? Date.now;
  let state = initialRoomConnectionState;
  let expiresAt = options.expiresAt;
  let disposed = false;
  let startTimer: ReturnType<typeof setTimeout> | undefined;
  let refreshTimer: ReturnType<typeof setTimeout> | undefined;

  const dispatch = (event: RoomConnectionEvent) => {
    if (disposed) return;
    const next = transitionRoomConnection(state, event);
    if (next === state) return;
    state = next;
    options.onChange(next);
    if (next.kind === "connecting") scheduleStart(next.attempt);
    if (next.kind === "expired") {
      clearTimeout(startTimer);
      clearTimeout(refreshTimer);
      options.stop().catch(() => undefined);
    }
  };

  const scheduleStart = (attempt: number) => {
    clearTimeout(startTimer);
    startTimer = setTimeout(() => {
      options.start().then(
        () => dispatch({ type: "started" }),
        () => dispatch({ type: "startFailed" }),
      );
    }, startRetryDelaysMs[attempt]);
  };

  const scheduleRefresh = (delay: number) => {
    clearTimeout(refreshTimer);
    refreshTimer = setTimeout(refresh, delay);
  };

  const refresh = () => {
    if (disposed) return;
    if (expiresAt.getTime() <= now()) {
      dispatch({ type: "tokenExpired" });
      return;
    }
    options.refreshToken().then(
      (renewedUntil) => {
        if (disposed || state.kind === "expired") return;
        expiresAt = renewedUntil;
        scheduleRefresh(refreshDelayMs(expiresAt.getTime() - now()));
      },
      () => {
        if (disposed || state.kind === "expired") return;
        const remaining = expiresAt.getTime() - now();
        if (remaining <= 0) dispatch({ type: "tokenExpired" });
        else scheduleRefresh(Math.min(refreshRetryMs, remaining));
      },
    );
  };

  return {
    begin: () => {
      if (disposed) return;
      scheduleStart(0);
      scheduleRefresh(refreshDelayMs(expiresAt.getTime() - now()));
    },
    dispatch,
    dispose: () => {
      disposed = true;
      clearTimeout(startTimer);
      clearTimeout(refreshTimer);
      options.stop().catch(() => undefined);
    },
  };
}
