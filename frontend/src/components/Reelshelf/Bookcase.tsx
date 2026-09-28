import type { CSSProperties } from "react";
import { IconPlus } from "@tabler/icons-react";
import {
  bookBinding,
  bookSize,
  spineTitle,
  type GameShelfItem,
} from "./reelshelf-model";
import { StitchedDot } from "./StitchedDot";

type BookcaseProps = {
  shelf: GameShelfItem[];
  selectedId: string | null;
  onSelect: (game: GameShelfItem) => void;
  onPutBack: () => void;
  onAddGame: () => void;
};

function plural(count: number, one: string, many: string) {
  return `${count} ${count === 1 ? one : many}`;
}

function describe(game: GameShelfItem) {
  if (game.clipCount === 0) return `${game.name}, no clips yet`;
  const clips = plural(game.clipCount, "clip", "clips");
  return game.unviewedCount > 0
    ? `${game.name}, ${clips}, ${game.unviewedCount} new`
    : `${game.name}, ${clips}`;
}

function Spine({
  game,
  onSelect,
}: {
  game: GameShelfItem;
  onSelect: () => void;
}) {
  const binding = bookBinding(game);
  const { width, height } = bookSize(game);
  const { series, title } = spineTitle(game.name);
  const empty = game.clipCount === 0;
  const titleSize = Math.max(
    14,
    Math.min(24, 28 - title.length * 0.6, width * 0.3),
  );

  return (
    <button
      type="button"
      className={`rs-book-spine${empty ? " empty" : ""}${binding.light ? " light" : ""}`}
      style={
        {
          "--cloth": binding.cloth,
          "--ink": binding.ink,
          "--book-w": `${width}px`,
          "--book-h": `${height}px`,
          "--title-size": `${titleSize}px`,
        } as CSSProperties
      }
      onClick={onSelect}
      aria-label={describe(game)}
    >
      {!empty && game.coverUrl ? (
        <img className="rs-book-weave" src={game.coverUrl} alt="" />
      ) : null}
      <span className="rs-book-bands" aria-hidden="true" />
      <span className="rs-book-title" aria-hidden="true">
        {series ? <span className="rs-book-series">{series}</span> : null}
        <span className="rs-book-name">{title}</span>
      </span>
      <span className="rs-book-foot" aria-hidden="true">
        {empty ? (
          <span className="rs-book-empty">Empty</span>
        ) : (
          <span className="rs-book-count">
            {game.clipCount}
            {game.unviewedCount > 0 ? <StitchedDot /> : null}
          </span>
        )}
        <span className="rs-book-rule" />
      </span>
      <span className="rs-book-tip" aria-hidden="true">
        <span className="rs-book-tip-name">{game.name}</span>
        <span className="rs-book-tip-meta">
          {empty ? "No clips yet" : plural(game.clipCount, "clip", "clips")}
          {game.unviewedCount > 0 ? (
            <>
              <StitchedDot />
              <strong>{game.unviewedCount} new</strong>
            </>
          ) : null}
        </span>
      </span>
    </button>
  );
}

function FaceOutBook({
  game,
  onPutBack,
}: {
  game: GameShelfItem;
  onPutBack: () => void;
}) {
  const binding = bookBinding(game);
  const empty = game.clipCount === 0;

  return (
    <button
      type="button"
      className={`rs-book-face${empty ? " empty" : ""}`}
      style={{ "--cloth": binding.cloth } as CSSProperties}
      onClick={onPutBack}
      aria-pressed="true"
      aria-label={`${describe(game)}. Pulled out; press to put it back on the shelf`}
    >
      <span className="rs-book-face-shadow" aria-hidden="true" />
      <span className="rs-book-face-body" aria-hidden="true">
        {game.unviewedCount > 0 ? (
          <span className="rs-book-face-tab">{game.unviewedCount} new</span>
        ) : null}
        <span className="rs-book-face-hinge" />
        <span className="rs-book-face-cover">
          {game.coverUrl ? (
            <img src={game.coverUrl} alt="" />
          ) : (
            <span className="rs-book-face-plain rs-display">{game.name}</span>
          )}
          {empty ? (
            <span className="rs-book-face-empty rs-display">No clips yet</span>
          ) : null}
        </span>
      </span>
    </button>
  );
}

/**
 * The library shelf: every game is a book bound in a colour from its cover. Choosing one pulls it
 * out and turns it face-out; choosing the pulled-out book puts it back.
 */
export function Bookcase({
  shelf,
  selectedId,
  onSelect,
  onPutBack,
  onAddGame,
}: BookcaseProps) {
  return (
    <div className="rs-bookcase">
      <div className="rs-bookcase-back" role="region" aria-label="Your shelf">
        <div className="rs-bookcase-row">
          {shelf.map((game) =>
            game.id === selectedId ? (
              <FaceOutBook key={game.id} game={game} onPutBack={onPutBack} />
            ) : (
              <Spine
                key={game.id}
                game={game}
                onSelect={() => onSelect(game)}
              />
            ),
          )}
          <button
            type="button"
            className="rs-book-add"
            onClick={onAddGame}
            aria-label="Add a game or category"
          >
            <IconPlus size={20} aria-hidden="true" />
            <span>Add a game</span>
          </button>
        </div>
      </div>
      <div className="rs-bookcase-plank" aria-hidden="true" />
    </div>
  );
}
