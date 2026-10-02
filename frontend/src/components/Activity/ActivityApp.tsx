import { useEffect, useState } from "react";
import { BrandLogo } from "@/components/Reelshelf/BrandLogo";
import { toApiError } from "@/shared/services/apiError";
import { ActivityClipPicker } from "./ActivityClipPicker";
import { ActivityPlayer } from "./ActivityPlayer";
import { ActivityQueue } from "./ActivityQueue";
import {
  ActivityReactionBar,
  ActivityReactionLayer,
} from "./ActivityReactions";
import { useWatchRoom, type WatchRoom } from "./useWatchRoom";
import {
  ActivityUnavailableError,
  activityImageUrl,
  connectToActivity,
  type ActivityConnection,
  type ActivityParticipantResponse,
} from "@/shared/services/discordActivity";

type ConnectState =
  | { status: "connecting" }
  | { status: "connected"; connection: ActivityConnection }
  | { status: "failed"; message: string };

/**
 * The watch room inside a Discord voice channel (ADR-0006): sign in through Discord, then join the room's
 * live connection. Members queue clips from their own shelf; the host controls playback and the queue, and
 * everyone else follows along.
 */
export function ActivityApp() {
  const [state, setState] = useState<ConnectState>({ status: "connecting" });
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    let cancelled = false;
    connectToActivity()
      .then((connection) => {
        if (!cancelled) setState({ status: "connected", connection });
      })
      .catch(async (error: unknown) => {
        const message = await describeFailure(error);
        if (!cancelled) setState({ status: "failed", message });
      });
    return () => {
      cancelled = true;
    };
  }, [attempt]);

  return (
    <main className="rs-activity">
      <header className="rs-activity-header">
        <span className="rs-brand">
          <BrandLogo />
        </span>
        <span className="rs-activity-kicker">Watch room</span>
      </header>

      {state.status === "connecting" ? (
        <section className="rs-public-state" aria-live="polite">
          <span className="rs-spinner" aria-hidden="true" />
          <p>Joining the room…</p>
        </section>
      ) : null}

      {state.status === "failed" ? (
        <section className="rs-public-state" aria-live="polite">
          <h1 className="rs-display rs-h2">{state.message}</h1>
          <button
            type="button"
            className="rs-primary"
            onClick={() => {
              setState({ status: "connecting" });
              setAttempt((value) => value + 1);
            }}
          >
            Try again
          </button>
        </section>
      ) : null}

      {state.status === "connected" ? (
        <ActivityRoom connection={state.connection} />
      ) : null}
    </main>
  );
}

function ActivityRoom({ connection }: { connection: ActivityConnection }) {
  const { session } = connection;
  const { participant } = session;
  const room = useWatchRoom(session);
  const playback = room.state?.playback ?? null;
  const host = room.state?.participants.find((person) => person.isHost);
  const avatarUrl = activityImageUrl(participant.avatarUrl);
  const queue = room.state?.queue ?? [];
  const canQueue =
    participant.isMember && (room.isHost || !room.state?.queueLocked);

  return (
    <section className="rs-activity-room">
      {room.status !== "connected" ? (
        <p className="rs-activity-banner" aria-live="polite">
          {room.status === "connecting"
            ? "Connecting to the room…"
            : "Reconnecting to the room…"}
        </p>
      ) : null}

      {playback ? (
        <div className="rs-activity-now">
          <ActivityPlayer
            playback={playback}
            clockOffsetMs={room.clockOffsetMs}
            overlay={
              <ActivityReactionLayer
                reactions={room.reactions}
                itemId={playback.itemId}
              />
            }
            host={
              room.isHost
                ? {
                    onPlay: room.play,
                    onPause: room.pause,
                    onSeek: room.seek,
                    onEnded: room.next,
                  }
                : null
            }
          />
          <div className="rs-activity-now-details">
            <div>
              <h2 className="rs-display rs-activity-now-title">
                {playback.clip.title}
              </h2>
              <p className="rs-activity-role">
                {[playback.clip.game, `from ${playback.clip.ownerName}'s shelf`]
                  .filter(Boolean)
                  .join(" · ")}
              </p>
            </div>
            {room.isHost ? (
              <button
                type="button"
                className="rs-primary"
                onClick={() => room.next(playback.itemId)}
              >
                {queue.length > 0 ? "Skip" : "Stop"}
              </button>
            ) : null}
          </div>
          <ActivityReactionBar onReact={room.react} />
        </div>
      ) : null}

      {room.error ? (
        <p className="rs-activity-banner" role="alert">
          {room.error}
        </p>
      ) : null}

      <div className="rs-activity-you">
        {avatarUrl ? (
          <img className="rs-activity-avatar" src={avatarUrl} alt="" />
        ) : null}
        <div>
          <h1 className="rs-display rs-h2">{participant.name}</h1>
          <p className="rs-activity-role">
            {describeRole(room, participant, host?.name)}
          </p>
        </div>
      </div>

      <div className="rs-activity-present">
        <h2 className="rs-activity-kicker">In the room</h2>
        <ul>
          {(room.state?.participants ?? []).map((person) => {
            const personAvatar = activityImageUrl(person.avatarUrl);
            return (
              <li key={person.discordUserId}>
                {personAvatar ? (
                  <img
                    className="rs-activity-avatar is-small"
                    src={personAvatar}
                    alt=""
                  />
                ) : (
                  <span
                    className="rs-activity-avatar is-small"
                    aria-hidden="true"
                  />
                )}
                <span>{person.name}</span>
                {person.isHost ? (
                  <span className="rs-activity-tag">Host</span>
                ) : !person.isMember ? (
                  <span className="rs-activity-tag is-quiet">Guest</span>
                ) : null}
              </li>
            );
          })}
        </ul>
      </div>

      {playback || queue.length > 0 || participant.isMember ? (
        <ActivityQueue
          room={room}
          viewerDiscordUserId={participant.discordUserId}
        />
      ) : null}

      {canQueue ? (
        <ActivityClipPicker
          roomToken={session.roomToken}
          queuedClipIds={new Set(queue.map((item) => item.clipId))}
          onPick={(clip) => room.addToQueue(clip.clipId)}
        />
      ) : null}
    </section>
  );
}

function describeRole(
  room: WatchRoom,
  participant: ActivityParticipantResponse,
  hostName: string | undefined,
): string {
  if (room.isHost) {
    return `You're hosting. Add clips from ${participant.accountName}'s shelf below and everyone watches them with you.`;
  }
  const hosting = hostName
    ? `${hostName} is hosting.`
    : "Nobody with a Reelshelf shelf is here yet.";
  if (!participant.isMember) {
    return `${hosting} You're watching as a guest. Have a Reelshelf account? Link Discord in Settings to share your clips.`;
  }
  return room.state?.queueLocked
    ? `${hosting} They've locked the queue for now.`
    : `${hosting} Add clips from your shelf to the queue below.`;
}

async function describeFailure(error: unknown): Promise<string> {
  if (error instanceof ActivityUnavailableError) return error.message;

  const apiError = await toApiError(error);
  if (apiError.status === 403) {
    return "Couldn't confirm you're in this Activity. Rejoin it from the voice channel.";
  }
  if (apiError.status === 503) return "The watch room isn't switched on yet.";
  return "Couldn't join the room.";
}
