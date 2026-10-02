import { useState } from "react";
import type { FormEvent } from "react";
import { useNavigate } from "@tanstack/react-router";
import { Dialog } from "@/components/Reelshelf/Dialog";
import { useDeleteAccount, useStorageUsage } from "@/hooks/queries";
import { ApiError } from "@/shared/services/apiError";
import { deleteAccountPhrase } from "@/shared/services/user";
import { formatFileSize } from "@/shared/utils/format";

/** The Settings control for permanently deleting the account, confirmed by typing a phrase. */
export function DeleteAccount() {
  const [open, setOpen] = useState(false);

  return (
    <>
      <div>
        <button
          className="rs-small-button rs-small-button-danger"
          type="button"
          onClick={() => setOpen(true)}
        >
          Delete account…
        </button>
      </div>
      {open ? <DeleteAccountDialog onClose={() => setOpen(false)} /> : null}
    </>
  );
}

function DeleteAccountDialog({ onClose }: { onClose: () => void }) {
  const [confirmation, setConfirmation] = useState("");
  const [error, setError] = useState<string | null>(null);
  const { data: storage } = useStorageUsage();
  const deleteAccount = useDeleteAccount();
  const navigate = useNavigate();
  const confirmed = confirmation.trim().toLowerCase() === deleteAccountPhrase;

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (!confirmed) return;
    setError(null);
    deleteAccount.mutate(confirmation, {
      onSuccess: () => {
        void navigate({
          to: "/sign-in",
          search: { account_deleted: true },
          replace: true,
        });
      },
      onError: (failure) => {
        setError(
          failure instanceof ApiError
            ? failure.message
            : "Your account couldn't be deleted. Try again.",
        );
      },
    });
  };

  return (
    <Dialog
      eyebrow="Delete account"
      title="Delete your account?"
      busy={deleteAccount.isPending}
      onClose={onClose}
    >
      {() => (
        <form className="rs-modal-panel" onSubmit={submit}>
          <div className="rs-modal-note rs-delete-account-note">
            <p>This permanently deletes, and can't be undone:</p>
            <ul className="rs-delete-account-list">
              <li>
                every clip you've uploaded or imported
                {storage && storage.usedBytes > 0
                  ? ` (${formatFileSize(storage.usedBytes)})`
                  : ""}
                , with its tags, detections and transcripts
              </li>
              <li>
                your share links, and the collections you made, including for
                their collaborators
              </li>
              <li>your linked Discord and Twitch sign-ins</li>
            </ul>
          </div>
          <label className="rs-field">
            <span>
              Type <strong>{deleteAccountPhrase}</strong> to confirm
            </span>
            <span className="rs-input-shell">
              <input
                value={confirmation}
                onChange={(event) => setConfirmation(event.currentTarget.value)}
                autoComplete="off"
                spellCheck={false}
                disabled={deleteAccount.isPending}
                autoFocus
              />
            </span>
          </label>
          {error ? (
            <p className="rs-modal-error rs-modal-error-inline" role="alert">
              {error}
            </p>
          ) : null}
          <div className="rs-modal-actions">
            <button
              className="rs-primary rs-primary-danger rs-modal-submit"
              type="submit"
              disabled={!confirmed || deleteAccount.isPending}
            >
              {deleteAccount.isPending
                ? "Deleting…"
                : "Permanently delete my account"}
            </button>
          </div>
        </form>
      )}
    </Dialog>
  );
}
