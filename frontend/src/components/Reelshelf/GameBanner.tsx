import type { CSSProperties } from "react";
import { IconPlus } from "@tabler/icons-react";
import { libraryCopy } from "@/copy/library";
import { StitchedDot } from "./StitchedDot";
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
          {libraryCopy.banner.eyebrow(game.isCustom)}
        </div>
        <h1 id="rs-game-banner-title" className="rs-display">
          {game.name}
        </h1>
        <p className="rs-game-banner-stats">
          {game.clipCount === 0 ? (
            libraryCopy.banner.empty
          ) : (
            <>
              <StatLine totals={totals} />
              {game.unviewedCount > 0 ? (
                <span className="rs-game-banner-new">
                  <StitchedDot />
                  {libraryCopy.newCount(game.unviewedCount)}
                </span>
              ) : null}
            </>
          )}
        </p>
      </div>
      <div className="rs-game-banner-actions">
        <button type="button" className="rs-primary" onClick={onUpload}>
          <IconPlus size={16} aria-hidden="true" />
          {libraryCopy.banner.addClips}
        </button>
        <button
          type="button"
          className="rs-game-banner-back"
          onClick={onPutBack}
        >
          {libraryCopy.banner.putBack}
        </button>
      </div>
    </section>
  );
}
