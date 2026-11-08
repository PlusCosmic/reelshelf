import { useState } from "react";
import type { FormEvent } from "react";
import { commonCopy } from "@/copy/common";
import { libraryCopy } from "@/copy/library";
import { ApiError } from "@/shared/services/apiError";
import { Dialog } from "@pluscosmic/ui";
import type { GameShelfItem } from "./reelshelf-model";

const copy = libraryCopy.removeBook;

/**
 * Asks before an empty book comes off the shelf. The API refuses a book that has clips by now (one
 * uploaded from another tab, say), and the dialog says so rather than closing.
 */
export function RemoveBookDialog({
  game,
  pending,
  onConfirm,
  onClose,
}: {
  game: GameShelfItem;
  pending: boolean;
  /** Takes the book off; resolves once the API has answered, rejecting with its error. */
  onConfirm: () => Promise<void>;
  onClose: () => void;
}) {
  const [error, setError] = useState<string | null>(null);

  const submit = (event: FormEvent) => {
    event.preventDefault();
    setError(null);
    onConfirm().then(onClose, (failure: unknown) =>
      setError(
        failure instanceof ApiError && failure.kind === "conflict"
          ? copy.hasClips(game.name)
          : copy.failed(game.name),
      ),
    );
  };

  return (
    <Dialog title={copy.title(game.name)} busy={pending} onClose={onClose}>
      {() => (
        <form className="rs-modal-panel" onSubmit={submit}>
          {/* Once the API has refused, "It has no clips" is no longer true. */}
          {error ? (
            <p className="rs-modal-error rs-modal-error-inline" role="alert">
              {error}
            </p>
          ) : (
            <p className="rs-modal-note">{copy.body}</p>
          )}
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
              className="rs-primary rs-primary-danger"
              type="submit"
              disabled={pending}
              autoFocus
            >
              {pending ? copy.removing : copy.confirm}
            </button>
          </div>
        </form>
      )}
    </Dialog>
  );
}
