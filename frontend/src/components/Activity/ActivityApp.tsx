import { useEffect, useState } from "react";
import { BrandLogo } from "@/components/Reelshelf/BrandLogo";
import { toApiError } from "@/shared/services/apiError";
import { ActivityClipPicker } from "./ActivityClipPicker";
import { ActivityPlayer } from "./ActivityPlayer";
import {
  ActivityUnavailableError,
  activityImageUrl,
  connectToActivity,
  toRoomPresence,
  type ActivityClip,
  type ActivityConnection,
  type RoomPresence,
} from "@/shared/services/discordActivity";

type ConnectState =
  | { status: "connecting" }
  | { status: "connected"; connection: ActivityConnection }
  | { status: "failed"; message: string };

/**
 * The watch room inside a Discord voice channel (ADR-0006). This first slice signs the participant in and
 * shows who is here; playback and the room queue build on the session it holds.
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
  const { sdk, session } = connection;
  const { participant } = session;
  const [present, setPresent] = useState<RoomPresence[]>([]);
  const [playing, setPlaying] = useState<ActivityClip | null>(null);

  useEffect(() => {
    let active = true;
    const update = (data: {
      participants: Parameters<typeof toRoomPresence>[0][];
    }) => {
      if (active) setPresent(data.participants.map(toRoomPresence));
    };

    void sdk.commands.getInstanceConnectedParticipants().then(update);
    void sdk.subscribe("ACTIVITY_INSTANCE_PARTICIPANTS_UPDATE", update);
    return () => {
      active = false;
      void sdk.unsubscribe("ACTIVITY_INSTANCE_PARTICIPANTS_UPDATE", update);
    };
  }, [sdk]);

  const avatarUrl = activityImageUrl(participant.avatarUrl);

  return (
    <section className="rs-activity-room">
      {playing ? (
        <div className="rs-activity-now">
          <ActivityPlayer videoId={playing.videoId} title={playing.title} />
          <div>
            <h2 className="rs-display rs-activity-now-title">
              {playing.title}
            </h2>
            {playing.game ? (
              <p className="rs-activity-role">{playing.game}</p>
            ) : null}
          </div>
        </div>
      ) : null}

      <div className="rs-activity-you">
        {avatarUrl ? (
          <img className="rs-activity-avatar" src={avatarUrl} alt="" />
        ) : null}
        <div>
          <h1 className="rs-display rs-h2">{participant.name}</h1>
          <p className="rs-activity-role">
            {participant.isMember
              ? `Your shelf: ${participant.accountName}. Pick a clip below to play it. Playback isn't shared with the room yet.`
              : "Watching as a guest. Have a Reelshelf account? Link Discord in Settings to queue your clips."}
          </p>
        </div>
      </div>

      <div className="rs-activity-present">
        <h2 className="rs-activity-kicker">In the room</h2>
        <ul>
          {present.map((person) => (
            <li key={person.id}>
              {person.avatarUrl ? (
                <img
                  className="rs-activity-avatar is-small"
                  src={person.avatarUrl}
                  alt=""
                />
              ) : (
                <span
                  className="rs-activity-avatar is-small"
                  aria-hidden="true"
                />
              )}
              <span>{person.name}</span>
            </li>
          ))}
        </ul>
      </div>

      {participant.isMember ? (
        <ActivityClipPicker
          roomToken={session.roomToken}
          playingClipId={playing?.clipId ?? null}
          onPick={setPlaying}
        />
      ) : null}
    </section>
  );
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
