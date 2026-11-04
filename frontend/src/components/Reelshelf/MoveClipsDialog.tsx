import { IconPlus } from "@tabler/icons-react";
import { binCopy } from "@/copy/bin";
import { commonCopy } from "@/copy/common";
import { Dialog } from "./Dialog";
import { bookBinding, type GameShelfItem } from "./reelshelf-model";

const copy = binCopy.move;

/**
 * Asks which book the chosen clips belong in: one of the shelf's other books, or another game, which opens the
 * add-a-game search. The move itself, and its undo note, are the caller's.
 */
export function MoveClipsDialog({
  count,
  books,
  pending,
  onMove,
  onAnotherGame,
  onClose,
}: {
  /** How many clips are moving, or "all" for every clip that matches the list. */
  count: number | "all";
  /** The books the clips can go to: the shelf, less the book they are in. */
  books: GameShelfItem[];
  pending: boolean;
  onMove: (book: GameShelfItem) => void;
  onAnotherGame: () => void;
  onClose: () => void;
}) {
  return (
    <Dialog
      eyebrow={copy.eyebrow}
      title={copy.title(count)}
      busy={pending}
      onClose={onClose}
    >
      {() => (
        <div className="rs-modal-panel">
          {books.length === 0 ? (
            <p className="rs-modal-note">{copy.noOtherBooks}</p>
          ) : (
            <ul className="rs-move-books">
              {books.map((book) => {
                const { cloth, ink } = bookBinding(book);
                return (
                  <li key={book.id}>
                    <button
                      type="button"
                      className="rs-move-book"
                      onClick={() => onMove(book)}
                      disabled={pending}
                      aria-label={copy.book(book.name, book.clipCount)}
                    >
                      <span
                        className="rs-move-book-cloth"
                        style={{ background: cloth, color: ink }}
                        aria-hidden="true"
                      />
                      <span className="rs-move-book-name">{book.name}</span>
                      <span className="rs-move-book-count" aria-hidden="true">
                        {book.clipCount.toLocaleString()}
                      </span>
                    </button>
                  </li>
                );
              })}
            </ul>
          )}
          {pending ? (
            <p className="rs-modal-note" role="status">
              {copy.moving}
            </p>
          ) : null}
          <div className="rs-modal-actions">
            <button
              className="rs-secondary"
              type="button"
              onClick={onClose}
              disabled={pending}
            >
              {commonCopy.cancel}
            </button>
            <button
              className="rs-secondary"
              type="button"
              onClick={onAnotherGame}
              disabled={pending}
            >
              <IconPlus size={14} aria-hidden="true" />
              {copy.anotherGame}
            </button>
          </div>
        </div>
      )}
    </Dialog>
  );
}
