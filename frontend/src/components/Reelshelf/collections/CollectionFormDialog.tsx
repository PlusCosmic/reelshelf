import type { FormEvent } from "react";
import { useState } from "react";
import { Dialog } from "../Dialog";

/** The name and description of a collection, for making a new one or editing one. */
export function CollectionFormDialog({
  title,
  submitLabel,
  savingLabel,
  initialName = "",
  initialDescription = "",
  saving,
  error,
  onSubmit,
  onClose,
  onDelete,
  deleting = false,
}: {
  title: string;
  submitLabel: string;
  savingLabel: string;
  initialName?: string;
  initialDescription?: string;
  saving: boolean;
  error: unknown;
  onSubmit: (name: string, description: string | null) => void;
  onClose: () => void;
  /** Offered when editing a collection its viewer owns. */
  onDelete?: () => void;
  deleting?: boolean;
}) {
  const [name, setName] = useState(initialName);
  const [description, setDescription] = useState(initialDescription);

  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!name.trim()) return;
    onSubmit(name.trim(), description.trim() || null);
  };

  return (
    <Dialog
      eyebrow="Collection"
      title={title}
      busy={saving || deleting}
      onClose={onClose}
    >
      {() => (
        <form className="rs-modal-panel" onSubmit={submit}>
          <label className="rs-field">
            <span>Name</span>
            <span className="rs-input-shell">
              <input
                value={name}
                onChange={(event) => setName(event.currentTarget.value)}
                placeholder="Ranked highlights, Friday night…"
                maxLength={100}
                required
                autoFocus
              />
            </span>
          </label>
          <label className="rs-field">
            <span>Description (optional)</span>
            <span className="rs-input-shell">
              <input
                value={description}
                onChange={(event) => setDescription(event.currentTarget.value)}
                placeholder="What goes in it"
                maxLength={500}
              />
            </span>
          </label>
          {error ? (
            <p className="rs-modal-error" role="alert">
              {error instanceof Error
                ? error.message
                : "The collection could not be saved."}
            </p>
          ) : null}
          <div className="rs-modal-actions">
            <button
              className="rs-primary rs-modal-submit"
              type="submit"
              disabled={!name.trim() || saving || deleting}
            >
              {saving ? savingLabel : submitLabel}
            </button>
            {onDelete ? (
              <button
                className="rs-small-button rs-small-button-danger"
                type="button"
                disabled={saving || deleting}
                onClick={onDelete}
              >
                {deleting ? "Deleting…" : "Delete collection"}
              </button>
            ) : null}
          </div>
        </form>
      )}
    </Dialog>
  );
}
