import { reactionEmoji } from "@/shared/services/watchRoom";
import type { FloatingReaction } from "./useWatchRoom";

/** Reactions to the playing clip, floating up over the video with the sender's name. */
export function ActivityReactionLayer({
  reactions,
  itemId,
}: {
  reactions: FloatingReaction[];
  itemId: string;
}) {
  return (
    <div className="rs-activity-reactions" aria-hidden="true">
      {reactions
        .filter((reaction) => reaction.itemId === itemId)
        .map((reaction) => (
          <span
            key={reaction.key}
            className="rs-activity-reaction"
            style={{ left: `${8 + reaction.lane * 11}%` }}
          >
            <span className="rs-activity-reaction-emoji">{reaction.emoji}</span>
            <span className="rs-activity-reaction-name">{reaction.name}</span>
          </span>
        ))}
    </div>
  );
}

/** The emoji anyone in the room, guests included, can send while a clip plays. */
export function ActivityReactionBar({
  onReact,
}: {
  onReact: (emoji: string) => void;
}) {
  return (
    <div className="rs-activity-reaction-bar" role="group" aria-label="React">
      {reactionEmoji.map((emoji) => (
        <button
          key={emoji}
          type="button"
          className="rs-icon-button"
          aria-label={`React ${emoji}`}
          onClick={() => onReact(emoji)}
        >
          {emoji}
        </button>
      ))}
    </div>
  );
}
