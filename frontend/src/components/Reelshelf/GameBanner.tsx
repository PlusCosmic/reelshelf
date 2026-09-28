import type { CSSProperties } from "react";
import { IconUpload } from "@tabler/icons-react";
import { StitchedDot } from "./Bookcase";
import { StatLine, type ClipTotals } from "./primitives/Stats";
import { bookBinding, type GameShelfItem } from "./reelshelf-model";

type GameBannerProps = {
  game: GameShelfItem;
  totals: ClipTotals;
  onUpload: () => void;
  onPutBack: () => void;
};

/**
 * The header for a pulled-out game: its key art behind a scrim with the cover beside the title, or,
 * for a game without key art, its cloth laid flat.
 */
export function GameBanner({
  game,
  totals,
  onUpload,
  onPutBack,
}: GameBannerProps) {
  const binding = bookBinding(game);
  const withArt = Boolean(game.keyArtUrl);

  return (
    <section
      className={`rs-game-banner${withArt ? " with-art" : " cloth"}${binding.light && !withArt ? " light" : ""}`}
      style={
        { "--cloth": binding.cloth, "--ink": binding.ink } as CSSProperties
      }
      aria-labelledby="rs-game-banner-title"
    >
      {withArt ? (
        <img className="rs-game-banner-art" src={game.keyArtUrl!} alt="" />
      ) : game.coverUrl && game.clipCount > 0 ? (
        <img className="rs-game-banner-weave" src={game.coverUrl} alt="" />
      ) : null}
      {game.coverUrl ? (
        <img
          className={`rs-game-banner-cover${game.clipCount === 0 ? " empty" : ""}`}
          src={game.coverUrl}
          alt=""
        />
      ) : null}
      <div className="rs-game-banner-text">
        <div className="rs-eyebrow">
          {game.isCustom ? "Custom category" : "Game category"}
        </div>
        <h1 id="rs-game-banner-title" className="rs-display">
          {game.name}
        </h1>
        <p className="rs-game-banner-stats">
          {game.clipCount === 0 ? (
            "No clips on this shelf yet"
          ) : (
            <>
              <StatLine totals={totals} />
              {game.unviewedCount > 0 ? (
                <span className="rs-game-banner-new">
                  <StitchedDot />
                  {game.unviewedCount} new
                </span>
              ) : null}
            </>
          )}
        </p>
      </div>
      <div className="rs-game-banner-actions">
        <button type="button" className="rs-primary" onClick={onUpload}>
          <IconUpload size={16} aria-hidden="true" />
          Upload clips
        </button>
        <button
          type="button"
          className="rs-game-banner-back"
          onClick={onPutBack}
        >
          Put it back on the shelf
        </button>
      </div>
    </section>
  );
}
