import type { CSSProperties } from "react";
import {
  useCallback,
  useEffect,
  useLayoutEffect,
  useRef,
  useState,
} from "react";
import {
  IconChevronLeft,
  IconChevronRight,
  IconPlus,
} from "@tabler/icons-react";
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
  /** Leave out for a shelf that only shows games, like the landing page's example. */
  onAddGame?: () => void;
};

type Tip = { game: GameShelfItem; left: number; top: number };

const ADD_KEY = "add-a-game";
// Quick enough to never hold anything up, long enough to see where things went.
const SHIFT_MS = 220;
const EASE = "cubic-bezier(0.2, 0.7, 0.2, 1)";

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

function reducedMotion() {
  return window.matchMedia("(prefers-reduced-motion: reduce)").matches;
}

function Spine({
  game,
  returning,
  onSelect,
  onShowTip,
  onHideTip,
  itemRef,
}: {
  game: GameShelfItem;
  returning: boolean;
  onSelect: () => void;
  onShowTip: (element: HTMLElement) => void;
  onHideTip: () => void;
  itemRef: (element: HTMLElement | null) => void;
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
      ref={itemRef}
      className={`rs-book-spine${empty ? " empty" : ""}${binding.light ? " light" : ""}${returning ? " returning" : ""}`}
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
      onPointerEnter={(event) => onShowTip(event.currentTarget)}
      onPointerLeave={onHideTip}
      onFocus={(event) => onShowTip(event.currentTarget)}
      onBlur={onHideTip}
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
    </button>
  );
}

function FaceOutBook({
  game,
  onPutBack,
  itemRef,
}: {
  game: GameShelfItem;
  onPutBack: () => void;
  itemRef: (element: HTMLElement | null) => void;
}) {
  const binding = bookBinding(game);
  const empty = game.clipCount === 0;

  return (
    <button
      type="button"
      ref={itemRef}
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
 * out and turns it face-out; choosing the pulled-out book puts it back. The other books slide along
 * to make room rather than jumping, and a shelf wider than the case scrolls, with buttons at
 * whichever end has more.
 */
export function Bookcase({
  shelf,
  selectedId,
  onSelect,
  onPutBack,
  onAddGame,
}: BookcaseProps) {
  const caseRef = useRef<HTMLDivElement>(null);
  const backRef = useRef<HTMLDivElement>(null);
  const tipRef = useRef<HTMLDivElement>(null);
  const items = useRef(new Map<string, HTMLElement>());
  const offsets = useRef(new Map<string, number>());
  const [tip, setTip] = useState<Tip | null>(null);
  const [overflow, setOverflow] = useState({ left: false, right: false });

  // The book that was pulled out until this render goes back with its own little turn. Worked
  // out while rendering, so the spine carries its class from the moment it reappears.
  const [previousId, setPreviousId] = useState(selectedId);
  const [returningId, setReturningId] = useState<string | null>(null);
  if (previousId !== selectedId) {
    setPreviousId(selectedId);
    setReturningId(previousId);
  }

  const itemRef = useCallback(
    (key: string) => (element: HTMLElement | null) => {
      if (element) items.current.set(key, element);
      else items.current.delete(key);
    },
    [],
  );

  // Slide the books from where they stood to where they stand now. Offsets are measured within
  // the row, so scrolling the shelf between choices never counts as movement.
  useLayoutEffect(() => {
    const previous = offsets.current;
    const next = new Map<string, number>();
    for (const [key, element] of items.current)
      next.set(key, element.offsetLeft);
    offsets.current = next;
    if (previous.size === 0 || reducedMotion()) return;
    for (const [key, element] of items.current) {
      const before = previous.get(key);
      const after = next.get(key);
      if (before === undefined || after === undefined || before === after)
        continue;
      element.animate(
        [{ translate: `${before - after}px 0` }, { translate: "0 0" }],
        { duration: SHIFT_MS, easing: EASE },
      );
    }
  }, [selectedId, shelf]);

  // Keep the pulled-out book wholly in view, on arrival and whenever another is chosen.
  const firstScroll = useRef(true);
  useLayoutEffect(() => {
    const back = backRef.current;
    const face = selectedId ? items.current.get(selectedId) : undefined;
    if (!back || !face) return;
    const margin = 24;
    const start = face.offsetLeft - margin;
    const end = face.offsetLeft + face.offsetWidth + margin;
    let target = back.scrollLeft;
    if (start < back.scrollLeft) target = start;
    else if (end > back.scrollLeft + back.clientWidth)
      target = end - back.clientWidth;
    if (target !== back.scrollLeft)
      back.scrollTo({
        left: target,
        behavior: firstScroll.current || reducedMotion() ? "auto" : "smooth",
      });
    firstScroll.current = false;
  }, [selectedId]);

  // Which ends of the shelf run past the case.
  useEffect(() => {
    const back = backRef.current;
    if (!back) return;
    const measure = () =>
      setOverflow({
        left: back.scrollLeft > 1,
        right: back.scrollLeft + back.clientWidth < back.scrollWidth - 1,
      });
    measure();
    const observer = new ResizeObserver(() => {
      measure();
      // A resize can rescale the books; start the next slide from where they stand now.
      for (const [key, element] of items.current)
        offsets.current.set(key, element.offsetLeft);
    });
    observer.observe(back);
    if (back.firstElementChild) observer.observe(back.firstElementChild);
    back.addEventListener("scroll", measure, { passive: true });
    return () => {
      observer.disconnect();
      back.removeEventListener("scroll", measure);
    };
  }, []);

  const scrollShelf = (direction: -1 | 1) => {
    const back = backRef.current;
    if (!back) return;
    back.scrollBy({
      left: direction * back.clientWidth * 0.7,
      behavior: reducedMotion() ? "auto" : "smooth",
    });
  };

  // The label lives outside the scrolling shelf, so neither end of the case can clip it.
  const showTip = (game: GameShelfItem) => (element: HTMLElement) => {
    const box = caseRef.current?.getBoundingClientRect();
    if (!box) return;
    const rect = element.getBoundingClientRect();
    setTip({
      game,
      left: rect.left + rect.width / 2 - box.left,
      top: rect.top - box.top,
    });
  };
  const hideTip = () => setTip(null);

  // Nudge the label back inside the case when it would hang off either end.
  useLayoutEffect(() => {
    const element = tipRef.current;
    const box = caseRef.current;
    if (!element || !box || !tip) return;
    const half = element.offsetWidth / 2;
    const clamped = Math.min(
      Math.max(tip.left, half + 8),
      box.clientWidth - half - 8,
    );
    element.style.left = `${clamped}px`;
  }, [tip]);

  return (
    <div className="rs-bookcase" ref={caseRef}>
      <div
        className="rs-bookcase-back"
        role="region"
        aria-label="Your shelf"
        ref={backRef}
        onScroll={hideTip}
      >
        <div className="rs-bookcase-row">
          {shelf.map((game) =>
            game.id === selectedId ? (
              <FaceOutBook
                key={game.id}
                game={game}
                onPutBack={onPutBack}
                itemRef={itemRef(game.id)}
              />
            ) : (
              <Spine
                key={game.id}
                game={game}
                returning={game.id === returningId}
                onSelect={() => {
                  hideTip();
                  onSelect(game);
                }}
                onShowTip={showTip(game)}
                onHideTip={hideTip}
                itemRef={itemRef(game.id)}
              />
            ),
          )}
          {onAddGame ? (
            <button
              type="button"
              ref={itemRef(ADD_KEY)}
              className="rs-book-add"
              onClick={onAddGame}
              aria-label="Add a game or category"
            >
              <IconPlus size={20} aria-hidden="true" />
              <span>Add a game</span>
            </button>
          ) : null}
        </div>
      </div>
      {overflow.left ? (
        <button
          type="button"
          className="rs-bookcase-scroll left"
          onClick={() => scrollShelf(-1)}
          aria-label="Scroll the shelf left"
        >
          <IconChevronLeft size={20} aria-hidden="true" />
        </button>
      ) : null}
      {overflow.right ? (
        <button
          type="button"
          className="rs-bookcase-scroll right"
          onClick={() => scrollShelf(1)}
          aria-label="Scroll the shelf right"
        >
          <IconChevronRight size={20} aria-hidden="true" />
        </button>
      ) : null}
      <div className="rs-bookcase-plank" aria-hidden="true" />
      {tip ? (
        <div
          className="rs-book-tip"
          ref={tipRef}
          style={{ left: tip.left, top: tip.top }}
          aria-hidden="true"
        >
          <span className="rs-book-tip-name">{tip.game.name}</span>
          <span className="rs-book-tip-meta">
            {tip.game.clipCount === 0
              ? "No clips yet"
              : plural(tip.game.clipCount, "clip", "clips")}
            {tip.game.unviewedCount > 0 ? (
              <>
                <StitchedDot />
                <strong>{tip.game.unviewedCount} new</strong>
              </>
            ) : null}
          </span>
        </div>
      ) : null}
    </div>
  );
}
