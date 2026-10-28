import { useState } from "react";
import { Link } from "@tanstack/react-router";
import { IconBrandTwitch, IconX } from "@tabler/icons-react";
import type { TwitchClipSummary } from "@/api-client";
import { useImportTwitchClips } from "./twitchSuggestions";
import { twitchImportCopy } from "@/copy/twitchImport";

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
    <section
      className="rs-twitch-strip"
      aria-label={twitchImportCopy.strip.label}
    >
      <IconBrandTwitch className="rs-twitch-strip-mark" size={20} />
      <div className="rs-twitch-strip-text">
        {importing ? (
          <strong>
            {twitchImportCopy.strip.bringingIn(
              Math.min(done + 1, total),
              total,
            )}
          </strong>
        ) : (
          <strong>{twitchImportCopy.strip.fromThisGame(clips.length)}</strong>
        )}
        <span className="rs-meta">
          {twitchImportCopy.strip.notOnShelf}
          {newest && !importing
            ? twitchImportCopy.strip.newest(
                newest.toLocaleDateString(undefined, {
                  day: "numeric",
                  month: "short",
                }),
              )
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
          {twitchImportCopy.strip.bringIn(clips.length)}
        </button>
        <Link
          to="/upload"
          search={{ source: "twitch" }}
          className="rs-secondary"
        >
          {twitchImportCopy.strip.choose}
        </Link>
        <button
          type="button"
          className="rs-icon-button"
          aria-label={twitchImportCopy.strip.notNow}
          onClick={() => setHidden(true)}
          disabled={importing}
        >
          <IconX size={16} />
        </button>
      </div>
    </section>
  );
}
