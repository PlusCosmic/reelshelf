import {
  IconArrowDown,
  IconArrowUp,
  IconLock,
  IconLockOpen,
  IconPlayerPlay,
  IconX,
} from "@tabler/icons-react";
import { formatDuration } from "@/components/Reelshelf/reelshelf-model";
import type { WatchRoom } from "./useWatchRoom";

/**
 * What plays next. Everyone sees the queue; the host reorders, plays, removes and locks it, and a member can
 * remove their own clips.
 */
export function ActivityQueue({
  room,
  viewerDiscordUserId,
}: {
  room: WatchRoom;
  viewerDiscordUserId: string;
}) {
  const queue = room.state?.queue ?? [];
  const locked = room.state?.queueLocked ?? false;

  return (
    <section className="rs-activity-queue">
      <div className="rs-activity-picker-head">
        <h2 className="rs-activity-kicker">
          Up next{locked ? " · locked" : ""}
        </h2>
        {room.isHost ? (
          <button
            type="button"
            className="rs-small-button"
            aria-pressed={locked}
            onClick={() => room.lockQueue(!locked)}
          >
            {locked ? (
              <IconLockOpen size={14} aria-hidden="true" />
            ) : (
              <IconLock size={14} aria-hidden="true" />
            )}
            {locked ? "Unlock queue" : "Lock queue"}
          </button>
        ) : null}
      </div>

      {queue.length === 0 ? (
        <p className="rs-activity-role">Nothing queued yet.</p>
      ) : (
        <ol className="rs-activity-queue-list">
          {queue.map((item, index) => {
            const canRemove =
              room.isHost || item.ownerDiscordUserId === viewerDiscordUserId;
            return (
              <li key={item.itemId}>
                <div className="rs-activity-queue-text">
                  <span className="rs-activity-clip-title">{item.title}</span>
                  <span className="rs-activity-clip-meta">
                    {[
                      item.game,
                      `${item.ownerName}'s shelf`,
                      formatDuration(item.durationSeconds),
                    ]
                      .filter(Boolean)
                      .join(" · ")}
                  </span>
                </div>
                <div className="rs-activity-queue-actions">
                  {room.isHost ? (
                    <>
                      <button
                        type="button"
                        className="rs-icon-button"
                        aria-label={`Play ${item.title} now`}
                        onClick={() => room.playNow(item.itemId)}
                      >
                        <IconPlayerPlay size={16} aria-hidden="true" />
                      </button>
                      <button
                        type="button"
                        className="rs-icon-button"
                        aria-label={`Move ${item.title} up`}
                        disabled={index === 0}
                        onClick={() => room.moveInQueue(item.itemId, index - 1)}
                      >
                        <IconArrowUp size={16} aria-hidden="true" />
                      </button>
                      <button
                        type="button"
                        className="rs-icon-button"
                        aria-label={`Move ${item.title} down`}
                        disabled={index === queue.length - 1}
                        onClick={() => room.moveInQueue(item.itemId, index + 1)}
                      >
                        <IconArrowDown size={16} aria-hidden="true" />
                      </button>
                    </>
                  ) : null}
                  {canRemove ? (
                    <button
                      type="button"
                      className="rs-icon-button"
                      aria-label={`Remove ${item.title} from the queue`}
                      onClick={() => room.removeFromQueue(item.itemId)}
                    >
                      <IconX size={16} aria-hidden="true" />
                    </button>
                  ) : null}
                </div>
              </li>
            );
          })}
        </ol>
      )}
    </section>
  );
}
