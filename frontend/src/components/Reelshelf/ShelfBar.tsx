import type { CSSProperties } from "react";
import { useEffect, useRef, useState } from "react";
import {
  IconArrowUp,
  IconCheck,
  IconChevronDown,
  IconSearch,
  IconX,
} from "@tabler/icons-react";
import { libraryCopy } from "@/copy/library";
import { bookBinding, type GameShelfItem } from "./reelshelf-model";
import { StitchedDot } from "./StitchedDot";

type ShelfBarProps = {
  /** Shown once the bookcase has scrolled out of view; until then the shelf itself is the control. */
  visible: boolean;
  shelf: GameShelfItem[];
  selected: GameShelfItem | null;
  unviewedTotal: number;
  clipTotal: number;
  query: string;
  onQueryChange: (value: string) => void;
  onChoose: (game: GameShelfItem | null) => void;
  onBackToShelf: () => void;
};

/** Three books on a plank: the "All games" mark, standing in for a single game's cover. */
function AllGamesMark() {
  return (
    <svg className="rs-shelfbar-mark" viewBox="0 0 26 35" aria-hidden="true">
      <rect x="1" y="5" width="7" height="27" rx="1" fill="var(--fill)" />
      <rect x="9.5" y="2" width="6" height="30" rx="1" fill="var(--accent)" />
      <rect
        x="17"
        y="8"
        width="6"
        height="24"
        rx="1"
        fill="var(--fill)"
        transform="rotate(8 20 32)"
      />
      <rect x="0" y="32" width="26" height="2.5" rx="1" fill="var(--fg)" />
    </svg>
  );
}

function GameMark({ game }: { game: GameShelfItem }) {
  const { cloth } = bookBinding(game);
  return (
    <span
      className={`rs-shelfbar-cover${game.clipCount === 0 ? " empty" : ""}`}
      style={{ "--cloth": cloth } as CSSProperties}
      aria-hidden="true"
    >
      {game.coverUrl ? <img src={game.coverUrl} alt="" /> : null}
    </span>
  );
}

/**
 * The bar that takes over from the bookcase once it has scrolled away: which game the list shows,
 * a switcher to choose another, search, and a way back up to the shelf.
 */
export function ShelfBar({
  visible,
  shelf,
  selected,
  unviewedTotal,
  clipTotal,
  query,
  onQueryChange,
  onChoose,
  onBackToShelf,
}: ShelfBarProps) {
  const [open, setOpen] = useState(false);
  const [searching, setSearching] = useState(false);
  const switcherRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (!open) return;
    const onPointerDown = (event: PointerEvent) => {
      if (!switcherRef.current?.contains(event.target as Node)) setOpen(false);
    };
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        setOpen(false);
        triggerRef.current?.focus();
      }
    };
    document.addEventListener("pointerdown", onPointerDown);
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("pointerdown", onPointerDown);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [open]);

  // Scrolling back up to the shelf hands control back to it.
  const isOpen = open && visible;

  const newCount = selected ? selected.unviewedCount : unviewedTotal;
  const name = selected?.name ?? libraryCopy.shelfBar.allGames;
  const choose = (game: GameShelfItem | null) => {
    setOpen(false);
    onChoose(game);
  };

  return (
    <div className={`rs-shelfbar-anchor${visible ? " visible" : ""}`}>
      <div
        className="rs-shelfbar"
        role="region"
        aria-label={libraryCopy.shelfBar.label}
        aria-hidden={!visible}
        inert={!visible}
        style={
          selected
            ? ({ "--cloth": bookBinding(selected).cloth } as CSSProperties)
            : undefined
        }
      >
        <div
          className={`rs-shelfbar-switcher${searching ? " searching" : ""}`}
          ref={switcherRef}
        >
          <button
            type="button"
            ref={triggerRef}
            className="rs-shelfbar-trigger"
            aria-expanded={isOpen}
            aria-controls="rs-shelfbar-games"
            aria-label={libraryCopy.shelfBar.trigger(name, newCount)}
            onClick={() => setOpen((value) => !value)}
          >
            {selected ? <GameMark game={selected} /> : <AllGamesMark />}
            <span className="rs-shelfbar-name rs-display">{name}</span>
            {newCount > 0 ? (
              <span className="rs-shelfbar-new">
                <StitchedDot />
                {libraryCopy.newCount(newCount)}
              </span>
            ) : null}
            <IconChevronDown
              className="rs-shelfbar-chevron"
              size={16}
              aria-hidden="true"
            />
          </button>

          {isOpen ? (
            <>
              <div
                className="rs-shelfbar-backdrop"
                aria-hidden="true"
                onClick={() => setOpen(false)}
              />
              <div
                id="rs-shelfbar-games"
                className="rs-shelfbar-menu"
                role="group"
                aria-label={libraryCopy.shelfBar.chooseGame}
              >
                <div className="rs-shelfbar-menu-head">
                  <span className="rs-display">
                    {libraryCopy.shelfBar.chooseGame}
                  </span>
                  <button
                    type="button"
                    className="rs-icon-button"
                    aria-label={libraryCopy.shelfBar.closeGames}
                    onClick={() => setOpen(false)}
                  >
                    <IconX size={18} aria-hidden="true" />
                  </button>
                </div>
                <button
                  type="button"
                  className={`rs-shelfbar-option${selected ? "" : " current"}`}
                  aria-pressed={!selected}
                  onClick={() => choose(null)}
                >
                  <AllGamesMark />
                  <span className="rs-shelfbar-option-name">
                    {libraryCopy.shelfBar.allGames}
                  </span>
                  <span className="rs-shelfbar-option-count">
                    {libraryCopy.shelfBar.optionClips(clipTotal)}
                  </span>
                  <span className="rs-shelfbar-option-new">
                    {unviewedTotal > 0 ? (
                      <>
                        <StitchedDot />
                        {libraryCopy.newCount(unviewedTotal)}
                      </>
                    ) : null}
                  </span>
                  <span className="rs-shelfbar-option-check">
                    {selected ? null : (
                      <IconCheck size={18} aria-hidden="true" />
                    )}
                  </span>
                </button>
                {shelf.map((game) => {
                  const current = game.id === selected?.id;
                  return (
                    <button
                      key={game.id}
                      type="button"
                      className={`rs-shelfbar-option${current ? " current" : ""}${game.clipCount === 0 ? " empty" : ""}`}
                      aria-pressed={current}
                      onClick={() => choose(game)}
                    >
                      <GameMark game={game} />
                      <span className="rs-shelfbar-option-name">
                        {game.name}
                      </span>
                      <span className="rs-shelfbar-option-count">
                        {libraryCopy.shelfBar.optionClips(game.clipCount)}
                      </span>
                      <span className="rs-shelfbar-option-new">
                        {game.unviewedCount > 0 ? (
                          <>
                            <StitchedDot />
                            {libraryCopy.newCount(game.unviewedCount)}
                          </>
                        ) : null}
                      </span>
                      <span className="rs-shelfbar-option-check">
                        {current ? (
                          <IconCheck size={18} aria-hidden="true" />
                        ) : null}
                      </span>
                    </button>
                  );
                })}
              </div>
            </>
          ) : null}
        </div>

        <label
          className={`rs-shelfbar-search${searching ? " open" : ""}`}
          aria-label={libraryCopy.shelfBar.searchLabel}
        >
          <IconSearch size={16} aria-hidden="true" />
          <input
            type="search"
            value={query}
            placeholder={libraryCopy.shelfBar.searchPlaceholder(selected?.name)}
            onChange={(event) => onQueryChange(event.target.value)}
          />
        </label>
        <button
          type="button"
          className="rs-shelfbar-search-toggle rs-icon-button"
          aria-label={
            searching
              ? libraryCopy.shelfBar.closeSearch
              : libraryCopy.shelfBar.searchLabel
          }
          aria-expanded={searching}
          onClick={() => setSearching((value) => !value)}
        >
          {searching ? (
            <IconX size={18} aria-hidden="true" />
          ) : (
            <IconSearch size={18} aria-hidden="true" />
          )}
        </button>

        <button
          type="button"
          className="rs-shelfbar-back"
          onClick={onBackToShelf}
        >
          <IconArrowUp size={16} aria-hidden="true" />
          <span>{libraryCopy.shelfBar.backToShelf}</span>
        </button>
      </div>
    </div>
  );
}
