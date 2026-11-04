import { Link } from "@tanstack/react-router";
import {
  IconArrowBackUp,
  IconArrowsExchange,
  IconTrash,
  IconX,
} from "@tabler/icons-react";
import type { ReactNode } from "react";
import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useEffectEvent,
  useMemo,
  useRef,
  useState,
} from "react";
import type { MovedClip } from "@/api-client";
import { binCopy } from "@/copy/bin";
import { useBinClips, useRestoreClips } from "@/hooks/bin.queries";
import { useMoveClips } from "@/hooks/clipMove.queries";
import type { BinTarget } from "@/shared/services/bin";
import type { MoveTarget } from "@/shared/services/clipMove";

const copy = binCopy.toast;
const moveCopy = binCopy.move;
/** The most clips one move call takes (the API's limit), so undoing a large move goes in parts. */
const MOVE_BATCH = 1000;
/** How long the undo note stays, unless it is being pointed at or has focus. */
const NOTICE_MS = 10_000;

type Notice =
  | {
      kind: "binned";
      clipIds: string[];
      /** Runs after an undo puts the clips back, such as returning to the clip that was binned. */
      onUndone?: (clipIds: string[]) => void;
    }
  | { kind: "restored"; count: number }
  | {
      kind: "moved";
      moved: MovedClip[];
      book: string;
      /** Clips left where they were, because the book already has the same video. */
      skipped: number;
    }
  | { kind: "movedBack"; count: number }
  | { kind: "error"; message: string };

type BinOptions = { onUndone?: (clipIds: string[]) => void };

type BinActions = {
  /** Moves clips to the bin and offers to undo it; resolves with the clips moved. */
  bin: (target: BinTarget, options?: BinOptions) => Promise<string[]>;
  /** Says clips were put back, after the bin page restores some. */
  announceRestored: (count: number) => void;
  pending: boolean;
  /** Moves clips to another book and offers to undo it; resolves with the clips moved. */
  move: (
    target: MoveTarget,
    book: { id: string; name: string },
  ) => Promise<MovedClip[]>;
  moving: boolean;
};

/** The undoable kinds of note, which Ctrl+Z or ⌘Z acts on. */
function canUndo(notice: Notice | null) {
  return notice?.kind === "binned" || notice?.kind === "moved";
}

const BinContext = createContext<BinActions | null>(null);

export function useBin(): BinActions {
  const actions = useContext(BinContext);
  if (!actions) throw new Error("useBin needs a BinProvider");
  return actions;
}

function isTyping(target: EventTarget | null) {
  return (
    target instanceof HTMLElement &&
    (target.isContentEditable ||
      ["INPUT", "TEXTAREA", "SELECT"].includes(target.tagName))
  );
}

/**
 * Deleting clips puts them in the bin, and this note, at the foot of every signed-in page, offers to undo it for a
 * few seconds (Ctrl+Z or ⌘Z does the same). It outlives the page the clips were binned from, so binning a clip and
 * moving on to the next can still be undone. Moving clips to another book is undone the same way, each clip going
 * back to the book it came from.
 */
export function BinProvider({ children }: { children: ReactNode }) {
  const binClips = useBinClips();
  const restoreClips = useRestoreClips();
  const moveClips = useMoveClips();
  const [notice, setNotice] = useState<Notice | null>(null);
  const [held, setHeld] = useState(false);
  const noticeId = useRef(0);
  const [shownId, setShownId] = useState(0);

  const show = useCallback((next: Notice) => {
    noticeId.current += 1;
    setShownId(noticeId.current);
    setNotice(next);
  }, []);

  const bin = useCallback(
    async (target: BinTarget, options?: BinOptions) => {
      try {
        const result = await binClips.mutateAsync(target);
        if (result.clipIds.length > 0)
          show({
            kind: "binned",
            clipIds: result.clipIds,
            onUndone: options?.onUndone,
          });
        return result.clipIds;
      } catch (error) {
        show({ kind: "error", message: copy.binFailed });
        throw error;
      }
    },
    [binClips, show],
  );

  const move = useCallback(
    async (target: MoveTarget, book: { id: string; name: string }) => {
      try {
        const result = await moveClips.mutateAsync({
          target,
          categoryId: book.id,
        });
        show(
          result.moved.length > 0 || result.skippedIds.length > 0
            ? {
                kind: "moved",
                moved: result.moved,
                book: book.name,
                skipped: result.skippedIds.length,
              }
            : { kind: "error", message: moveCopy.nothingMoved(book.name) },
        );
        return result.moved;
      } catch (error) {
        show({ kind: "error", message: moveCopy.failed });
        throw error;
      }
    },
    [moveClips, show],
  );

  // Closing the note lets go of it too: focus or a pointer on a note that is gone mustn't keep the next one up.
  const close = useCallback(() => {
    setNotice(null);
    setHeld(false);
  }, []);

  const announceRestored = useCallback(
    (count: number) => show({ kind: "restored", count }),
    [show],
  );

  const undoMove = async (moved: MovedClip[]) => {
    const byBook = new Map<string, MovedClip[]>();
    for (const clip of moved) {
      const clips = byBook.get(clip.fromCategoryId) ?? [];
      clips.push(clip);
      byBook.set(clip.fromCategoryId, clips);
    }
    let count = 0;
    for (const [categoryId, clips] of byBook) {
      for (let at = 0; at < clips.length; at += MOVE_BATCH) {
        const result = await moveClips.mutateAsync({
          target: {
            clipIds: clips
              .slice(at, at + MOVE_BATCH)
              .map((clip) => clip.clipId),
          },
          categoryId,
        });
        count += result.moved.length;
      }
    }
    return count;
  };

  const undo = useEffectEvent(async () => {
    if (notice?.kind === "moved") {
      if (moveClips.isPending) return;
      const undoing = noticeId.current;
      try {
        const count = await undoMove(notice.moved);
        // As with binning: a newer note keeps its own undo rather than giving way to this one.
        if (noticeId.current === undoing) show({ kind: "movedBack", count });
      } catch {
        show({ kind: "error", message: moveCopy.undoFailed });
      }
      return;
    }
    if (notice?.kind !== "binned" || restoreClips.isPending) return;
    const undoing = noticeId.current;
    try {
      const result = await restoreClips.mutateAsync(notice.clipIds);
      notice.onUndone?.(result.clipIds);
      // A newer note (clips binned meanwhile) keeps its own undo rather than giving way to this one.
      if (noticeId.current === undoing)
        show({ kind: "restored", count: result.clipIds.length });
    } catch {
      show({ kind: "error", message: copy.undoFailed });
    }
  });

  // The note goes after a while, but never from under a pointer or keyboard focus.
  useEffect(() => {
    if (!notice || held) return;
    const timer = window.setTimeout(close, NOTICE_MS);
    return () => window.clearTimeout(timer);
  }, [notice, held, shownId, close]);

  const onKeyDown = useEffectEvent((event: KeyboardEvent) => {
    if (!canUndo(notice) || isTyping(event.target)) return;
    if (
      (event.ctrlKey || event.metaKey) &&
      !event.shiftKey &&
      event.key.toLowerCase() === "z"
    ) {
      event.preventDefault();
      void undo();
    }
  });
  useEffect(() => {
    const listener = (event: KeyboardEvent) => onKeyDown(event);
    window.addEventListener("keydown", listener);
    return () => window.removeEventListener("keydown", listener);
  }, []);

  const actions = useMemo(
    () => ({
      bin,
      announceRestored,
      pending: binClips.isPending,
      move,
      moving: moveClips.isPending,
    }),
    [bin, announceRestored, binClips.isPending, move, moveClips.isPending],
  );

  return (
    <BinContext.Provider value={actions}>
      {children}
      <div
        className="rs-bin-toast-layer"
        role="status"
        aria-live="polite"
        onPointerEnter={() => setHeld(true)}
        onPointerLeave={() => setHeld(false)}
        onFocus={() => setHeld(true)}
        onBlur={() => setHeld(false)}
      >
        {notice ? (
          <div
            key={shownId}
            className={`rs-bin-toast${notice.kind === "error" ? " error" : ""}`}
          >
            {notice.kind === "binned" ? (
              <IconTrash size={16} aria-hidden="true" />
            ) : notice.kind === "moved" ? (
              <IconArrowsExchange size={16} aria-hidden="true" />
            ) : null}
            <span className="rs-bin-toast-message">
              {noticeMessage(notice)}
            </span>
            {notice.kind === "moved" && notice.moved.length > 0 ? (
              <button
                type="button"
                className="rs-bin-toast-action"
                onClick={() => void undo()}
                disabled={moveClips.isPending}
              >
                <IconArrowBackUp size={15} aria-hidden="true" />
                {moveClips.isPending ? moveCopy.undoing : copy.undo}
                <kbd>{copy.undoHint}</kbd>
              </button>
            ) : null}
            {notice.kind === "binned" ? (
              <>
                <button
                  type="button"
                  className="rs-bin-toast-action"
                  onClick={() => void undo()}
                  disabled={restoreClips.isPending}
                >
                  <IconArrowBackUp size={15} aria-hidden="true" />
                  {restoreClips.isPending ? copy.undoing : copy.undo}
                  <kbd>{copy.undoHint}</kbd>
                </button>
                <Link to="/bin" className="rs-bin-toast-link" onClick={close}>
                  {copy.openBin}
                </Link>
              </>
            ) : null}
            <button
              type="button"
              className="rs-icon-button rs-bin-toast-close"
              aria-label={copy.dismiss}
              onClick={close}
            >
              <IconX size={14} />
            </button>
          </div>
        ) : null}
      </div>
    </BinContext.Provider>
  );
}

function noticeMessage(notice: Notice): string {
  switch (notice.kind) {
    case "binned":
      return copy.binned(notice.clipIds.length);
    case "restored":
      return copy.restored(notice.count);
    case "moved": {
      const moved = moveCopy.moved(notice.moved.length, notice.book);
      return notice.skipped > 0
        ? `${notice.moved.length > 0 ? `${moved} ` : ""}${moveCopy.skipped(notice.skipped)}`
        : moved;
    }
    case "movedBack":
      return moveCopy.movedBack(notice.count);
    case "error":
      return notice.message;
  }
}
