import { useCallback, useEffect, useRef, useState } from "react";
import type { HubConnection } from "@microsoft/signalr";
import { activityCopy } from "@/copy/activity";
import type { ActivitySessionResponse } from "@/shared/services/discordActivity";
import {
  createWatchRoomConnection,
  isConnected,
  refreshRoomToken,
  type RoomReaction,
  type RoomStateView,
} from "@/shared/services/watchRoom";
import {
  createRoomConnectionDriver,
  initialRoomConnectionState,
  type RoomConnectionDriver,
  type RoomConnectionStatus,
} from "./roomConnection";

export type WatchRoomStatus = RoomConnectionStatus;

/** How long a reaction stays on screen; matches the float animation in reelshelf.css. */
const reactionLifetimeMs = 2600;

/** More than this on screen at once and the oldest make way. */
const maxReactionsOnScreen = 30;

/** A reaction on screen, with a lane across the player so a burst spreads out. */
export type FloatingReaction = RoomReaction & { key: number; lane: number };

export type WatchRoom = {
  state: RoomStateView | null;
  status: WatchRoomStatus;
  /**
   * Whether the room is connected. Room controls are disabled otherwise: the state on screen may be stale
   * and nothing sent would arrive.
   */
  live: boolean;
  /** Tries the connection again after it `failed`. */
  retry: () => void;
  isHost: boolean;
  /** Server time minus local time, from the latest room state. */
  clockOffsetMs: () => number;
  error: string | null;
  /**
   * The current room token, read at call time. It is renewed before it expires, so anything that calls the
   * API with it (the clip picker) asks here on every request instead of keeping the one the session began with.
   */
  getRoomToken: () => string;
  reactions: FloatingReaction[];
  react: (emoji: string) => void;
  addToQueue: (clipId: string) => void;
  removeFromQueue: (itemId: string) => void;
  moveInQueue: (itemId: string, toIndex: number) => void;
  lockQueue: (locked: boolean) => void;
  playNow: (itemId: string) => void;
  /** Moves on from `currentItemId`; with null, from whatever is playing. */
  next: (currentItemId: string | null) => void;
  play: (positionSeconds: number) => void;
  pause: (positionSeconds: number) => void;
  seek: (positionSeconds: number) => void;
};

/** The live connection to this Activity instance's watch room. */
export function useWatchRoom(session: ActivitySessionResponse): WatchRoom {
  const [state, setState] = useState<RoomStateView | null>(null);
  const [status, setStatus] = useState<WatchRoomStatus>(
    initialRoomConnectionState.kind,
  );
  const [error, setError] = useState<string | null>(null);
  const [reactions, setReactions] = useState<FloatingReaction[]>([]);
  const tokenRef = useRef(session.roomToken);
  const initialExpiresAtRef = useRef(session.roomTokenExpiresAt);
  const offsetRef = useRef(0);
  const versionRef = useRef(-1);
  const connectionRef = useRef<HubConnection | null>(null);
  const driverRef = useRef<RoomConnectionDriver | null>(null);
  const getRoomToken = useCallback(() => tokenRef.current, []);

  useEffect(() => {
    let disposed = false;
    let reactionCount = 0;
    const reactionTimers = new Set<number>();

    const connection = createWatchRoomConnection(() => tokenRef.current);
    connectionRef.current = connection;

    // Retries a failed first connection, renews the token and gives up once it has expired (roomConnection.ts).
    const driver = createRoomConnectionDriver({
      start: () => {
        // A new connection may join a room that restarted and counts from zero again.
        versionRef.current = -1;
        return connection.start();
      },
      stop: () => connection.stop(),
      refreshToken: async () => {
        const refreshed = await refreshRoomToken(tokenRef.current);
        tokenRef.current = refreshed.roomToken;
        return refreshed.roomTokenExpiresAt;
      },
      expiresAt: initialExpiresAtRef.current,
      onChange: (next) => {
        if (!disposed) setStatus(next.kind);
      },
    });
    driverRef.current = driver;

    connection.on("RoomState", (next: RoomStateView) => {
      // States can overtake each other; keep the newest. A rejoined room after a restart counts from zero
      // again, which is why a reconnect resets the version.
      if (next.version <= versionRef.current) return;
      versionRef.current = next.version;
      offsetRef.current = Date.parse(next.serverTime) - Date.now();
      setState(next);
    });
    connection.on("Reaction", (reaction: RoomReaction) => {
      const key = reactionCount++;
      setReactions((current) =>
        [...current, { ...reaction, key, lane: key % 8 }].slice(
          -maxReactionsOnScreen,
        ),
      );
      const timer = window.setTimeout(() => {
        reactionTimers.delete(timer);
        setReactions((current) => current.filter((item) => item.key !== key));
      }, reactionLifetimeMs);
      reactionTimers.add(timer);
    });
    connection.onreconnecting(() => driver.dispatch({ type: "lost" }));
    connection.onreconnected(() => {
      versionRef.current = -1;
      driver.dispatch({ type: "reconnected" });
    });
    connection.onclose(() => driver.dispatch({ type: "closed" }));

    driver.begin();

    return () => {
      disposed = true;
      reactionTimers.forEach((timer) => window.clearTimeout(timer));
      connectionRef.current = null;
      driverRef.current = null;
      driver.dispose();
    };
  }, []);

  const invoke = useCallback((method: string, ...args: unknown[]) => {
    const connection = connectionRef.current;
    if (!connection || !isConnected(connection)) return;
    setError(null);
    connection.invoke(method, ...args).catch((failure: unknown) => {
      // HubException messages arrive prefixed with SignalR's own wording.
      const message =
        failure instanceof Error
          ? failure.message.replace(/^.*HubException: /, "")
          : activityCopy.room.somethingWentWrong;
      setError(message);
    });
  }, []);

  const retry = useCallback(() => {
    driverRef.current?.dispatch({ type: "retry" });
  }, []);

  return {
    state,
    status,
    live: status === "connected",
    retry,
    isHost: state?.hostDiscordUserId === session.participant.discordUserId,
    clockOffsetMs: () => offsetRef.current,
    error,
    getRoomToken,
    reactions,
    react: (emoji) => invoke("React", emoji),
    addToQueue: (clipId) => invoke("AddToQueue", clipId),
    removeFromQueue: (itemId) => invoke("RemoveFromQueue", itemId),
    moveInQueue: (itemId, toIndex) => invoke("MoveInQueue", itemId, toIndex),
    lockQueue: (locked) => invoke("LockQueue", locked),
    playNow: (itemId) => invoke("PlayNow", itemId),
    next: (currentItemId) => invoke("Next", currentItemId),
    play: (position) => invoke("Play", position),
    pause: (position) => invoke("Pause", position),
    seek: (position) => invoke("Seek", position),
  };
}
