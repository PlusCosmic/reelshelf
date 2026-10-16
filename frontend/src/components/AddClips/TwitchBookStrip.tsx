import { useState } from "react";
import { Link } from "@tanstack/react-router";
import { IconBrandTwitch, IconX } from "@tabler/icons-react";
import type { TwitchClipSummary } from "@/api-client";
import { useImportTwitchClips } from "./twitchSuggestions";

const SHOWN = 5;

/**
 * Inside an open book: the owner's Twitch clips of this game that aren't on the shelf yet, ready
 * to bring in with one press. "Not now" hides it until the next visit.
 */
export function TwitchBookStrip({
  categoryId,
  clips,
}: {
  categoryId: string;
  clips: TwitchClipSummary[];
}) {
  const [hidden, setHidden] = useState(false);
  const { importClips, importing, done } = useImportTwitchClips(categoryId);
  const [total, setTotal] = useState(0);

  if (hidden || (!clips.length && !importing)) return null;

  const newest = clips.reduce<Date | null>(
    (latest, clip) =>
      !latest || clip.createdAt > latest ? clip.createdAt : latest,
    null,
  );

  return (
    <section className="rs-twitch-strip" aria-label="Twitch clips to bring in">
      <IconBrandTwitch className="rs-twitch-strip-mark" size={20} />
      <div className="rs-twitch-strip-text">
        {importing ? (
          <strong>
            Bringing in {Math.min(done + 1, total)} of {total} Twitch clips…
          </strong>
        ) : (
          <strong>
            {clips.length === 1
              ? "1 of your Twitch clips is from this game"
              : `${clips.length} of your Twitch clips are from this game`}
          </strong>
        )}
        <span className="rs-meta">
          Not on your shelf yet
          {newest && !importing
            ? ` · newest ${newest.toLocaleDateString(undefined, { day: "numeric", month: "short" })}`
            : ""}
        </span>
      </div>
      <div className="rs-twitch-strip-thumbs" aria-hidden="true">
        {clips.slice(0, SHOWN).map((clip) => (
          <span key={clip.id} className="rs-twitch-strip-thumb">
            {clip.thumbnailUrl ? (
              <img src={clip.thumbnailUrl} alt="" loading="lazy" />
            ) : null}
            <span>{clip.title}</span>
          </span>
        ))}
      </div>
      <div className="rs-twitch-strip-actions">
        <button
          type="button"
          className="rs-primary"
          disabled={importing}
          onClick={() => {
            setTotal(clips.length);
            importClips(clips);
          }}
        >
          {clips.length === 1 ? "Bring it in" : `Bring in all ${clips.length}`}
        </button>
        <Link
          to="/upload"
          search={{ source: "twitch" }}
          className="rs-secondary"
        >
          Choose…
        </Link>
        <button
          type="button"
          className="rs-icon-button"
          aria-label="Not now"
          onClick={() => setHidden(true)}
          disabled={importing}
        >
          <IconX size={16} />
        </button>
      </div>
    </section>
  );
}
