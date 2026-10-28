import { useState } from "react";
import type { FormEvent } from "react";
import { useNavigate } from "@tanstack/react-router";
import { Dialog } from "@/components/Reelshelf/Dialog";
import { useDeleteAccount, usePlan } from "@/hooks/queries";
import { ApiError } from "@/shared/services/apiError";
import { deleteAccountPhrase } from "@/shared/services/user";
import { commonCopy } from "@/copy/common";
import { settingsCopy } from "@/copy/settings";

const copy = settingsCopy.deleteAccount;

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
          {copy.open}
        </button>
      </div>
      {open ? <DeleteAccountDialog onClose={() => setOpen(false)} /> : null}
    </>
  );
}

function DeleteAccountDialog({ onClose }: { onClose: () => void }) {
  const [confirmation, setConfirmation] = useState("");
  const [error, setError] = useState<string | null>(null);
  const { data: plan } = usePlan();
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
        setError(failure instanceof ApiError ? failure.message : copy.failed);
      },
    });
  };

  return (
    <Dialog
      eyebrow={copy.eyebrow}
      title={copy.title}
      busy={deleteAccount.isPending}
      onClose={onClose}
    >
      {() => (
        <form className="rs-modal-panel" onSubmit={submit}>
          <div className="rs-modal-note rs-delete-account-note">
            <p>{copy.intro}</p>
            <ul className="rs-delete-account-list">
              <li>
                {copy.clips(
                  plan && plan.clipCount > 0
                    ? copy.clipsSummary(plan.clipCount)
                    : "",
                )}
              </li>
              <li>{copy.collections}</li>
              {plan?.subscription &&
              !["canceled", "incomplete_expired"].includes(
                plan.subscription.status,
              ) ? (
                <li>{copy.paidPlan}</li>
              ) : null}
              <li>{copy.signIns}</li>
            </ul>
          </div>
          <label className="rs-field">
            <span>{copy.confirm(deleteAccountPhrase)}</span>
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
              {deleteAccount.isPending ? commonCopy.deleting : copy.submit}
            </button>
          </div>
        </form>
      )}
    </Dialog>
  );
}
