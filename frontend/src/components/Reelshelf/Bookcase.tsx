import type {
  CSSProperties,
  DragEvent,
  FocusEvent,
  KeyboardEvent,
  PointerEvent,
  ReactNode,
} from "react";
import {
  useCallback,
  useEffect,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
} from "react";
import {
  IconBrandTwitch,
  IconChevronLeft,
  IconChevronRight,
  IconPlus,
  IconX,
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
  /** Shown under the shelf, beside the IGDB credit, like the landing page's caption. */
  footer?: ReactNode;
  /**
   * The owner is arranging the shelf: every book stands spine-out, drags along the shelf (or moves with
   * the arrow keys) and empty ones carry a button that takes them off. Twitch books and Add a game wait.
   */
  arranging?: boolean;
  /** A move finished: the shelf's books, left to right. */
  onReorder?: (ids: string[]) => void;
  onRemoveBook?: (game: GameShelfItem) => void;
};

const igdbUrl = "https://www.igdb.com";

type Tip = { game: GameShelfItem; left: number; top: number };
type GhostCard = { ghost: TwitchGhost; left: number; top: number };
type DropTarget = { key: string; left: number; top: number };

/** A book being dragged along the shelf while it is arranged. */
type Drag = {
  id: string;
  pointerId: number;
  startX: number;
  clientX: number;
  /** Where on the book it was picked up, so it stays under the pointer. */
  grabOffset: number;
  moved: boolean;
  order: string[];
  frame: number | null;
};

/** An order being shown ahead of the shelf it was made from, until the shelf catches up. */
type Arrangement = { ids: string[]; shelf: GameShelfItem[] };

// A pointer that moves less than this is a press, not a drag.
const DRAG_SLOP = 6;
// How close to either end of the case a dragged book starts scrolling the shelf.
const SCROLL_EDGE = 56;

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

type ArrangeHandlers = {
  label: string;
  onPointerDown: (event: PointerEvent<HTMLButtonElement>) => void;
  onPointerMove: (event: PointerEvent<HTMLButtonElement>) => void;
  onPointerUp: (event: PointerEvent<HTMLButtonElement>) => void;
  onPointerCancel: (event: PointerEvent<HTMLButtonElement>) => void;
  onKeyDown: (event: KeyboardEvent<HTMLButtonElement>) => void;
};

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
  arrange,
}: {
  game: GameShelfItem;
  returning: boolean;
  dropTarget: boolean;
  twitchCount: number;
  drop: ReturnType<typeof dropHandlers>;
  onSelect: () => void;
  onShowTip: (element: HTMLElement) => void;
  onHideTip: () => void;
  itemRef?: (element: HTMLElement | null) => void;
  /** Set while the shelf is arranged: the book moves rather than opens. */
  arrange?: ArrangeHandlers;
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
      {...(arrange
        ? {
            onPointerDown: arrange.onPointerDown,
            onPointerMove: arrange.onPointerMove,
            onPointerUp: arrange.onPointerUp,
            onPointerCancel: arrange.onPointerCancel,
            onKeyDown: arrange.onKeyDown,
            "aria-label": arrange.label,
          }
        : {
            onClick: onSelect,
            onPointerEnter: (event: PointerEvent<HTMLElement>) =>
              onShowTip(event.currentTarget),
            onPointerLeave: onHideTip,
            onFocus: (event: FocusEvent<HTMLElement>) =>
              onShowTip(event.currentTarget),
            onBlur: onHideTip,
            "aria-label": libraryCopy.bookcase.spine(
              game.name,
              game.clipCount,
              game.unviewedCount,
              twitchCount,
            ),
            ...drop,
          })}
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
  footer,
  arranging = false,
  onReorder,
  onRemoveBook,
}: BookcaseProps) {
  const caseRef = useRef<HTMLDivElement>(null);
  const backRef = useRef<HTMLDivElement>(null);
  const rowRef = useRef<HTMLDivElement>(null);
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

  // Arranging stands every book spine-out, the pulled-out one included.
  const shownSelectedId = arranging ? null : selectedId;

  // The book that was pulled out until this render goes back with its own little turn. Worked
  // out while rendering, so the spine carries its class from the moment it reappears.
  const [previousId, setPreviousId] = useState(shownSelectedId);
  const [returningId, setReturningId] = useState<string | null>(null);
  if (previousId !== shownSelectedId) {
    setPreviousId(shownSelectedId);
    setReturningId(previousId);
  }

  // While a book is dragged, and from a move until the library hands back a shelf in that order, the
  // books stand in the order being made. Any new shelf ends it: the saved order, or the old one back.
  const [arrangement, setArrangement] = useState<Arrangement | null>(null);
  if (arrangement && arrangement.shelf !== shelf) setArrangement(null);
  const books = useMemo(() => {
    if (!arrangement || arrangement.shelf !== shelf) return shelf;
    const byId = new Map(shelf.map((game) => [game.id, game]));
    return arrangement.ids
      .map((id) => byId.get(id))
      .filter((game): game is GameShelfItem => game !== undefined);
  }, [arrangement, shelf]);
  const drag = useRef<Drag | null>(null);
  // Where a dropped book was last seen, so it settles into its place from there.
  const settling = useRef<{ id: string; left: number } | null>(null);
  const focusAfterMove = useRef<string | null>(null);
  const [announcement, setAnnouncement] = useState("");

  // Leaving arrange mode mid-drag drops the drag where it is, without a move.
  useEffect(() => {
    if (arranging) return;
    const state = drag.current;
    drag.current = null;
    if (state?.frame != null) cancelAnimationFrame(state.frame);
  }, [arranging]);

  const itemRef = useCallback(
    (key: string) => (element: HTMLElement | null) => {
      if (element) items.current.set(key, element);
      else items.current.delete(key);
    },
    [],
  );

  /** Keeps the dragged book under the pointer, wherever its slot on the shelf is now. */
  const placeDragged = useCallback(() => {
    const state = drag.current;
    const row = rowRef.current;
    const element = state ? items.current.get(state.id) : undefined;
    if (!state?.moved || !row || !element) return;
    const left =
      state.clientX - row.getBoundingClientRect().left - state.grabOffset;
    element.style.translate = `${left - element.offsetLeft}px 0`;
  }, []);

  // Slide the books from where they stood to where they stand now. Offsets are measured within
  // the row, so scrolling the shelf between choices never counts as movement.
  useLayoutEffect(() => {
    const previous = offsets.current;
    const next = new Map<string, number>();
    for (const [key, element] of items.current)
      next.set(key, element.offsetLeft);
    offsets.current = next;
    const held = drag.current?.moved ? drag.current.id : null;
    const settled = settling.current;
    settling.current = null;
    if (!reducedMotion()) {
      for (const [key, element] of items.current) {
        const after = next.get(key);
        const before = settled?.id === key ? settled.left : previous.get(key);
        if (key === held || before === undefined || after === undefined)
          continue;
        if (before === after) continue;
        element.animate(
          [{ translate: `${before - after}px 0` }, { translate: "0 0" }],
          { duration: SHIFT_MS, easing: EASE },
        );
      }
    }
    placeDragged();
    // A book moved from the keyboard keeps focus, and stays in view, in its new place.
    const focusId = focusAfterMove.current;
    focusAfterMove.current = null;
    const moved = focusId ? items.current.get(focusId) : undefined;
    const spine = moved?.querySelector<HTMLElement>(".rs-book-spine");
    if (spine && document.activeElement !== spine)
      spine.focus({ preventScroll: true });
    moved?.scrollIntoView({ block: "nearest", inline: "nearest" });
  }, [shownSelectedId, books, arranging, placeDragged]);

  // Keep the pulled-out book wholly in view, on arrival and whenever another is chosen.
  const firstScroll = useRef(true);
  useLayoutEffect(() => {
    const back = backRef.current;
    const face = shownSelectedId
      ? items.current.get(shownSelectedId)
      : undefined;
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
  }, [shownSelectedId]);

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

  /** Where the dragged book would stand: past the middle of every book its middle has crossed. */
  const dropIndex = (state: Drag) => {
    const row = rowRef.current;
    const dragged = items.current.get(state.id);
    if (!row || !dragged) return state.order.indexOf(state.id);
    const width = dragged.offsetWidth;
    const gap = parseFloat(getComputedStyle(row).columnGap) || 0;
    const middle =
      state.clientX -
      row.getBoundingClientRect().left -
      state.grabOffset +
      width / 2;
    const from = state.order.indexOf(state.id);
    let index = 0;
    state.order.forEach((id, at) => {
      const element = items.current.get(id);
      if (id === state.id || !element) return;
      // Measured as if the dragged book were lifted out, so its own width never flips the answer.
      const left =
        at > from ? element.offsetLeft - width - gap : element.offsetLeft;
      if (left + element.offsetWidth / 2 < middle) index += 1;
    });
    return index;
  };

  const followPointer = () => {
    const state = drag.current;
    if (!state) return;
    const index = dropIndex(state);
    const others = state.order.filter((id) => id !== state.id);
    const order = [...others.slice(0, index), state.id, ...others.slice(index)];
    if (order.join() === state.order.join()) return placeDragged();
    state.order = order;
    setArrangement({ ids: order, shelf });
  };

  // Near either end of the case, a dragged book scrolls the shelf, faster the closer it gets.
  const scrollWhileDragging = () => {
    const state = drag.current;
    const back = backRef.current;
    if (!state?.moved || !back) return;
    const box = back.getBoundingClientRect();
    const delta =
      state.clientX < box.left + SCROLL_EDGE
        ? -Math.ceil((box.left + SCROLL_EDGE - state.clientX) / 4)
        : state.clientX > box.right - SCROLL_EDGE
          ? Math.ceil((state.clientX - box.right + SCROLL_EDGE) / 4)
          : 0;
    if (delta) {
      back.scrollLeft += delta;
      followPointer();
    }
    state.frame = requestAnimationFrame(scrollWhileDragging);
  };

  const endDrag = (commit: boolean) => {
    const state = drag.current;
    drag.current = null;
    if (!state) return;
    if (state.frame !== null) cancelAnimationFrame(state.frame);
    if (!state.moved) return;
    const element = items.current.get(state.id);
    if (element) {
      settling.current = {
        id: state.id,
        left: element.offsetLeft + (parseFloat(element.style.translate) || 0),
      };
      element.style.translate = "";
      element.classList.remove("dragging");
    }
    const ids = shelf.map((game) => game.id);
    if (!commit || state.order.join() === ids.join()) {
      setArrangement(null);
      // Nothing re-rendered the books if they never left their places; settle the dropped one here.
      if (settling.current && element && !reducedMotion()) {
        const from = settling.current.left - element.offsetLeft;
        settling.current = null;
        if (from)
          element.animate(
            [{ translate: `${from}px 0` }, { translate: "0 0" }],
            {
              duration: SHIFT_MS,
              easing: EASE,
            },
          );
      }
      return;
    }
    setArrangement({ ids: state.order, shelf });
    const game = shelf.find((item) => item.id === state.id);
    if (game)
      setAnnouncement(
        libraryCopy.bookcase.arrange.moved(
          game.name,
          state.order.indexOf(state.id) + 1,
          state.order.length,
        ),
      );
    onReorder?.(state.order);
  };

  const moveFromKeyboard = (game: GameShelfItem, to: number) => {
    const ids = books.map((item) => item.id);
    const from = ids.indexOf(game.id);
    const target = Math.max(0, Math.min(ids.length - 1, to));
    if (from === target) return;
    ids.splice(from, 1);
    ids.splice(target, 0, game.id);
    focusAfterMove.current = game.id;
    setArrangement({ ids, shelf });
    setAnnouncement(
      libraryCopy.bookcase.arrange.moved(game.name, target + 1, ids.length),
    );
    onReorder?.(ids);
  };

  const arrangeHandlers = (
    game: GameShelfItem,
    index: number,
  ): ArrangeHandlers => ({
    label: libraryCopy.bookcase.arrange.spine(
      game.name,
      game.clipCount,
      game.unviewedCount,
      index + 1,
      books.length,
    ),
    onPointerDown: (event) => {
      if (event.button !== 0 || drag.current) return;
      const slot = items.current.get(game.id);
      if (!slot) return;
      event.currentTarget.setPointerCapture(event.pointerId);
      drag.current = {
        id: game.id,
        pointerId: event.pointerId,
        startX: event.clientX,
        clientX: event.clientX,
        grabOffset: event.clientX - slot.getBoundingClientRect().left,
        moved: false,
        order: books.map((item) => item.id),
        frame: null,
      };
    },
    onPointerMove: (event) => {
      const state = drag.current;
      if (!state || state.pointerId !== event.pointerId) return;
      state.clientX = event.clientX;
      if (!state.moved) {
        if (Math.abs(event.clientX - state.startX) < DRAG_SLOP) return;
        state.moved = true;
        items.current.get(state.id)?.classList.add("dragging");
        state.frame = requestAnimationFrame(scrollWhileDragging);
      }
      followPointer();
    },
    onPointerUp: (event) => {
      if (drag.current?.pointerId === event.pointerId) endDrag(true);
    },
    onPointerCancel: (event) => {
      if (drag.current?.pointerId === event.pointerId) endDrag(false);
    },
    onKeyDown: (event) => {
      const at = books.indexOf(game);
      const to =
        event.key === "ArrowLeft"
          ? at - 1
          : event.key === "ArrowRight"
            ? at + 1
            : event.key === "Home"
              ? 0
              : event.key === "End"
                ? books.length - 1
                : null;
      if (to !== null) {
        event.preventDefault();
        moveFromKeyboard(game, to);
      } else if (
        (event.key === "Delete" || event.key === "Backspace") &&
        game.clipCount === 0 &&
        onRemoveBook
      ) {
        event.preventDefault();
        onRemoveBook(game);
      }
    },
  });

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
    <div
      className={`rs-bookcase${arranging ? " arranging" : ""}`}
      ref={caseRef}
    >
      <div
        className="rs-bookcase-back"
        role="region"
        aria-label={libraryCopy.bookcase.label}
        ref={backRef}
        onScroll={hideTip}
      >
        <div className="rs-bookcase-row" ref={rowRef}>
          {books.map((game, index) =>
            arranging ? (
              <span
                key={game.id}
                className="rs-book-slot"
                ref={itemRef(game.id)}
              >
                <Spine
                  game={game}
                  returning={game.id === returningId}
                  dropTarget={false}
                  twitchCount={0}
                  drop={{}}
                  onSelect={() => {}}
                  onShowTip={() => {}}
                  onHideTip={() => {}}
                  arrange={arrangeHandlers(game, index)}
                />
                {game.clipCount === 0 && onRemoveBook ? (
                  <button
                    type="button"
                    className="rs-book-remove"
                    onClick={() => onRemoveBook(game)}
                    aria-label={libraryCopy.bookcase.arrange.remove(game.name)}
                  >
                    <IconX size={14} aria-hidden="true" />
                  </button>
                ) : null}
              </span>
            ) : game.id === shownSelectedId ? (
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
          {ghosts.length && !arranging ? (
            <span className="rs-shelf-divider" aria-hidden="true" />
          ) : null}
          {(arranging ? [] : ghosts).map((ghost) => {
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
          {onAddGame && !arranging ? (
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
      {/* IGDB's terms ask for credit wherever its covers and data appear. The library scrolls without
          end, so the credit sits under the shelf rather than in a footer nobody reaches. */}
      <div className="rs-bookcase-under">
        {footer ? <div className="rs-bookcase-footer">{footer}</div> : null}
        <p className="rs-igdb-credit">
          {libraryCopy.bookcase.igdbCredit(
            <a href={igdbUrl} target="_blank" rel="noopener noreferrer">
              {libraryCopy.bookcase.igdb}
            </a>,
          )}
        </p>
      </div>
      <p className="rs-visually-hidden" aria-live="polite">
        {announcement}
      </p>
      {tip && !arranging ? (
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
