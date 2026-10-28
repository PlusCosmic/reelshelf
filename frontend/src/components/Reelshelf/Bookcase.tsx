import type { CSSProperties, DragEvent } from "react";
import {
  useCallback,
  useEffect,
  useLayoutEffect,
  useRef,
  useState,
} from "react";
import {
  IconBrandTwitch,
  IconChevronLeft,
  IconChevronRight,
  IconPlus,
} from "@tabler/icons-react";
import type { TwitchGhost } from "@/components/AddClips/addClipsModel";
import { libraryCopy } from "@/copy/library";
import { dataTransferHasFiles } from "@/utils/bulkUploadDrop";
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
  /** Files are being dragged over the page: every book, and New book, takes a drop. */
  dropping?: boolean;
  onDropOnBook?: (game: GameShelfItem, data: DataTransfer) => void;
  onDropOnNewBook?: (data: DataTransfer) => void;
  /** Twitch clips waiting to come in, by book; a ribbon marks each book that has some. */
  twitchCounts?: ReadonlyMap<string, number>;
  /** Games with Twitch clips and no book yet, shown as outlined books at the end of the shelf. */
  ghosts?: TwitchGhost[];
  onAddGhost?: (ghost: TwitchGhost) => void;
  onDismissGhost?: (ghost: TwitchGhost) => void;
  onDropOnGhost?: (ghost: TwitchGhost, data: DataTransfer) => void;
};

type Tip = { game: GameShelfItem; left: number; top: number };
type GhostCard = { ghost: TwitchGhost; left: number; top: number };
type DropTarget = { key: string; left: number; top: number };

/** Drag handlers that make an element take dropped files, marking it while files are over it. */
function dropHandlers(
  key: string,
  onTarget: (key: string | null, element?: HTMLElement) => void,
  onDrop: ((data: DataTransfer) => void) | undefined,
) {
  if (!onDrop) return {};
  return {
    onDragOver: (event: DragEvent<HTMLElement>) => {
      if (!dataTransferHasFiles(event.dataTransfer)) return;
      event.preventDefault();
      event.stopPropagation();
      event.dataTransfer.dropEffect = "copy";
      onTarget(key, event.currentTarget);
    },
    onDragLeave: (event: DragEvent<HTMLElement>) => {
      if (event.currentTarget.contains(event.relatedTarget as Node | null))
        return;
      onTarget(null);
    },
    onDrop: (event: DragEvent<HTMLElement>) => {
      if (!dataTransferHasFiles(event.dataTransfer)) return;
      event.preventDefault();
      event.stopPropagation();
      onTarget(null);
      onDrop(event.dataTransfer);
    },
  };
}

function ghostSize(ghost: TwitchGhost) {
  const fullness = Math.sqrt(Math.min(ghost.clips.length, 300) / 300);
  return {
    width: Math.round(46 + 44 * fullness),
    height: 236 + (ghost.igdbId % 65),
  };
}

const ADD_KEY = "add-a-game";
// Quick enough to never hold anything up, long enough to see where things went.
const SHIFT_MS = 220;
const EASE = "cubic-bezier(0.2, 0.7, 0.2, 1)";

function reducedMotion() {
  return window.matchMedia("(prefers-reduced-motion: reduce)").matches;
}

function Spine({
  game,
  returning,
  dropTarget,
  twitchCount,
  drop,
  onSelect,
  onShowTip,
  onHideTip,
  itemRef,
}: {
  game: GameShelfItem;
  returning: boolean;
  dropTarget: boolean;
  twitchCount: number;
  drop: ReturnType<typeof dropHandlers>;
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
      className={`rs-book-spine${empty ? " empty" : ""}${binding.light ? " light" : ""}${returning ? " returning" : ""}${dropTarget ? " drop-target" : ""}`}
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
      aria-label={libraryCopy.bookcase.spine(
        game.name,
        game.clipCount,
        game.unviewedCount,
        twitchCount,
      )}
      {...drop}
    >
      {twitchCount > 0 ? (
        <span className="rs-book-ribbon" aria-hidden="true" />
      ) : null}
      {!empty && game.coverUrl ? (
        <img
          className="rs-book-weave"
          src={game.coverUrl}
          alt=""
          loading="lazy"
          decoding="async"
        />
      ) : null}
      <span className="rs-book-bands" aria-hidden="true" />
      <span className="rs-book-title" aria-hidden="true">
        {series ? <span className="rs-book-series">{series}</span> : null}
        <span className="rs-book-name">{title}</span>
      </span>
      <span className="rs-book-foot" aria-hidden="true">
        {empty ? (
          <span className="rs-book-empty">{libraryCopy.bookcase.empty}</span>
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
      aria-label={libraryCopy.bookcase.faceOut(
        game.name,
        game.clipCount,
        game.unviewedCount,
      )}
    >
      <span className="rs-book-face-shadow" aria-hidden="true" />
      <span className="rs-book-face-body" aria-hidden="true">
        {game.unviewedCount > 0 ? (
          <span className="rs-book-face-tab">
            {libraryCopy.newCount(game.unviewedCount)}
          </span>
        ) : null}
        <span className="rs-book-face-hinge" />
        <span className="rs-book-face-cover">
          {game.coverUrl ? (
            <img src={game.coverUrl} alt="" loading="lazy" decoding="async" />
          ) : (
            <span className="rs-book-face-plain rs-display">{game.name}</span>
          )}
          {empty ? (
            <span className="rs-book-face-empty rs-display">
              {libraryCopy.noClipsYet}
            </span>
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
  dropping = false,
  onDropOnBook,
  onDropOnNewBook,
  twitchCounts,
  ghosts = [],
  onAddGhost,
  onDismissGhost,
  onDropOnGhost,
}: BookcaseProps) {
  const caseRef = useRef<HTMLDivElement>(null);
  const backRef = useRef<HTMLDivElement>(null);
  const tipRef = useRef<HTMLDivElement>(null);
  const items = useRef(new Map<string, HTMLElement>());
  const offsets = useRef(new Map<string, number>());
  const [tip, setTip] = useState<Tip | null>(null);
  const [ghostCard, setGhostCard] = useState<GhostCard | null>(null);
  const [dropTarget, setDropTarget] = useState<DropTarget | null>(null);
  // A drag that leaves the page ends without a drop; forget which book it was over.
  if (!dropping && dropTarget) setDropTarget(null);
  const dropKey = dropping ? (dropTarget?.key ?? null) : null;
  const setDropKey = (key: string | null, element?: HTMLElement) => {
    if (!key || !element) return setDropTarget(null);
    if (dropTarget?.key === key) return;
    const box = caseRef.current?.getBoundingClientRect();
    if (!box) return;
    const rect = element.getBoundingClientRect();
    setDropTarget({
      key,
      left: rect.left + rect.width / 2 - box.left,
      top: rect.top - box.top,
    });
  };
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

  const openGhost = (ghost: TwitchGhost, element: HTMLElement) => {
    const box = caseRef.current?.getBoundingClientRect();
    if (!box) return;
    const rect = element.getBoundingClientRect();
    hideTip();
    setGhostCard({
      ghost,
      left: rect.left + rect.width / 2 - box.left,
      top: rect.top - box.top,
    });
  };

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
        aria-label={libraryCopy.bookcase.label}
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
                dropTarget={dropping && dropKey === game.id}
                twitchCount={twitchCounts?.get(game.id) ?? 0}
                drop={dropHandlers(
                  game.id,
                  setDropKey,
                  onDropOnBook && ((data) => onDropOnBook(game, data)),
                )}
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
          {ghosts.length ? (
            <span className="rs-shelf-divider" aria-hidden="true" />
          ) : null}
          {ghosts.map((ghost) => {
            const size = ghostSize(ghost);
            const key = `ghost-${ghost.igdbId}`;
            return (
              <button
                key={key}
                type="button"
                ref={itemRef(key)}
                className={`rs-book-spine rs-book-ghost${dropping && dropKey === key ? " drop-target" : ""}`}
                style={
                  {
                    "--book-w": `${size.width}px`,
                    "--book-h": `${size.height}px`,
                    "--title-size": `${Math.max(14, Math.min(22, 28 - ghost.gameName.length * 0.6, size.width * 0.3))}px`,
                  } as CSSProperties
                }
                onClick={(event) => openGhost(ghost, event.currentTarget)}
                aria-haspopup="dialog"
                aria-label={libraryCopy.bookcase.ghost(
                  ghost.gameName,
                  ghost.clips.length,
                )}
                {...dropHandlers(
                  key,
                  setDropKey,
                  onDropOnGhost && ((data) => onDropOnGhost(ghost, data)),
                )}
              >
                <span className="rs-book-bands" aria-hidden="true" />
                <span className="rs-book-title" aria-hidden="true">
                  <span className="rs-book-name">{ghost.gameName}</span>
                </span>
                <span className="rs-book-foot" aria-hidden="true">
                  <span className="rs-book-ghost-count">
                    <IconBrandTwitch size={13} />
                    {ghost.clips.length}
                  </span>
                  <span className="rs-book-rule" />
                </span>
              </button>
            );
          })}
          {onAddGame ? (
            <button
              type="button"
              ref={itemRef(ADD_KEY)}
              className={`rs-book-add${dropping ? " dropping" : ""}${dropping && dropKey === ADD_KEY ? " drop-target" : ""}`}
              onClick={onAddGame}
              aria-label={libraryCopy.bookcase.addGame}
              {...dropHandlers(ADD_KEY, setDropKey, onDropOnNewBook)}
            >
              <IconPlus size={20} aria-hidden="true" />
              <span>
                {dropping
                  ? libraryCopy.bookcase.newBook
                  : libraryCopy.bookcase.addGameShort}
              </span>
            </button>
          ) : null}
        </div>
      </div>
      {overflow.left ? (
        <button
          type="button"
          className="rs-bookcase-scroll left"
          onClick={() => scrollShelf(-1)}
          aria-label={libraryCopy.bookcase.scrollLeft}
        >
          <IconChevronLeft size={20} aria-hidden="true" />
        </button>
      ) : null}
      {overflow.right ? (
        <button
          type="button"
          className="rs-bookcase-scroll right"
          onClick={() => scrollShelf(1)}
          aria-label={libraryCopy.bookcase.scrollRight}
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
            {libraryCopy.bookcase.tipClips(tip.game.clipCount)}
            {tip.game.unviewedCount > 0 ? (
              <>
                <StitchedDot />
                <strong>{libraryCopy.newCount(tip.game.unviewedCount)}</strong>
              </>
            ) : null}
          </span>
          {twitchCounts?.get(tip.game.id) ? (
            <span className="rs-book-tip-meta rs-book-tip-twitch">
              <IconBrandTwitch size={13} />
              {libraryCopy.bookcase.tipTwitch(
                twitchCounts.get(tip.game.id) ?? 0,
              )}
            </span>
          ) : null}
        </div>
      ) : null}
      {dropKey && dropTarget ? (
        <DropLabel
          left={dropTarget.left}
          top={dropTarget.top}
          label={
            dropKey === ADD_KEY
              ? libraryCopy.bookcase.dropNewBook
              : dropKey.startsWith("ghost-")
                ? libraryCopy.bookcase.dropOnGhost(
                    ghosts.find((g) => `ghost-${g.igdbId}` === dropKey)
                      ?.gameName,
                  )
                : libraryCopy.bookcase.dropOnBook(
                    shelf.find((g) => g.id === dropKey)?.name,
                  )
          }
        />
      ) : null}
      {ghostCard ? (
        <div
          className="rs-ghost-card"
          role="dialog"
          aria-label={ghostCard.ghost.gameName}
          style={{ left: ghostCard.left, top: ghostCard.top }}
          onKeyDown={(event) => {
            if (event.key === "Escape") setGhostCard(null);
          }}
          onBlur={(event) => {
            if (!event.currentTarget.contains(event.relatedTarget))
              setGhostCard(null);
          }}
        >
          <span className="rs-book-tip-name">{ghostCard.ghost.gameName}</span>
          <span className="rs-ghost-card-meta">
            {libraryCopy.bookcase.ghostCard.meta(ghostCard.ghost.clips.length)}
          </span>
          <span className="rs-ghost-card-actions">
            <button
              type="button"
              className="rs-ghost-add"
              autoFocus
              onClick={() => {
                setGhostCard(null);
                onAddGhost?.(ghostCard.ghost);
              }}
            >
              {libraryCopy.bookcase.ghostCard.add}
            </button>
            <button
              type="button"
              onClick={() => {
                setGhostCard(null);
                onDismissGhost?.(ghostCard.ghost);
              }}
            >
              {libraryCopy.bookcase.ghostCard.dismiss}
            </button>
          </span>
        </div>
      ) : null}
    </div>
  );
}

/** The label over whichever book files are being dragged onto. */
function DropLabel({
  left,
  top,
  label,
}: {
  left: number;
  top: number;
  label: string;
}) {
  return (
    <div
      className="rs-book-tip rs-drop-label"
      style={{ left, top }}
      aria-hidden="true"
    >
      <span className="rs-book-tip-name">{label}</span>
    </div>
  );
}
