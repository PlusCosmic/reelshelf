import { useState } from "react";
import type { FormEvent } from "react";
import { IconFlag } from "@tabler/icons-react";
import { Dialog } from "@/components/Reelshelf/Dialog";
import { useReportSharedClip } from "@/hooks/queries";
import { ApiError } from "@/shared/services/apiError";
import {
  reportReasons,
  type ReportReason,
} from "@/shared/services/contentReports";

/** The share page's control for reporting the clip; anyone with the link can use it, signed in or not. */
export function ReportClip({ token }: { token: string }) {
  const [open, setOpen] = useState(false);

  return (
    <>
      <div>
        <button
          className="rs-small-button"
          type="button"
          onClick={() => setOpen(true)}
        >
          <IconFlag size={14} aria-hidden="true" />
          Report this clip
        </button>
      </div>
      {open ? (
        <ReportClipDialog token={token} onClose={() => setOpen(false)} />
      ) : null}
    </>
  );
}

function ReportClipDialog({
  token,
  onClose,
}: {
  token: string;
  onClose: () => void;
}) {
  const [reason, setReason] = useState<ReportReason | null>(null);
  const [details, setDetails] = useState("");
  const [email, setEmail] = useState("");
  const [error, setError] = useState<string | null>(null);
  const report = useReportSharedClip(token);

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (!reason) return;
    setError(null);
    report.mutate(
      { reason, details, email },
      {
        onError: (failure) => {
          setError(
            failure instanceof ApiError
              ? failure.message
              : "The report couldn't be sent. Try again.",
          );
        },
      },
    );
  };

  if (report.isSuccess) {
    return (
      <Dialog eyebrow="Report" title="Thanks for telling us" onClose={onClose}>
        {() => (
          <div className="rs-modal-panel">
            <p className="rs-modal-note">
              We'll review this clip and remove it if it breaks our terms.
              {reason === "child_safety" || reason === "terrorism"
                ? " Its link has stopped working while we do."
                : ""}
            </p>
            <div className="rs-modal-actions">
              <button
                className="rs-primary rs-modal-submit"
                type="button"
                onClick={onClose}
              >
                Close
              </button>
            </div>
          </div>
        )}
      </Dialog>
    );
  }

  return (
    <Dialog
      eyebrow="Report"
      title="What's wrong with this clip?"
      busy={report.isPending}
      onClose={onClose}
    >
      {() => (
        <form className="rs-modal-panel" onSubmit={submit}>
          <fieldset className="rs-report-reasons" disabled={report.isPending}>
            <legend className="rs-visually-hidden">Reason</legend>
            {reportReasons.map((item) => (
              <label key={item.value} className="rs-report-reason">
                <input
                  type="radio"
                  name="reason"
                  value={item.value}
                  checked={reason === item.value}
                  onChange={() => setReason(item.value)}
                />
                <span>{item.label}</span>
              </label>
            ))}
          </fieldset>
          {reason === "child_safety" ? (
            <p className="rs-modal-note">
              You can also report it to the{" "}
              <a
                href="https://report.iwf.org.uk/"
                target="_blank"
                rel="noreferrer"
              >
                Internet Watch Foundation
              </a>{" "}
              or, in the US,{" "}
              <a
                href="https://report.cybertip.org/"
                target="_blank"
                rel="noreferrer"
              >
                NCMEC's CyberTipline
              </a>
              . Please don't download or share it.
            </p>
          ) : null}
          <label className="rs-field">
            <span>Anything else we should know? (optional)</span>
            <textarea
              className="rs-report-details"
              value={details}
              maxLength={2000}
              rows={3}
              onChange={(event) => setDetails(event.currentTarget.value)}
              disabled={report.isPending}
            />
          </label>
          <label className="rs-field">
            <span>
              Your email, if you'd like to hear back (optional)
              {reason === "copyright"
                ? ". For copyright, include it so we can follow up"
                : ""}
            </span>
            <span className="rs-input-shell">
              <input
                type="email"
                value={email}
                maxLength={254}
                autoComplete="email"
                onChange={(event) => setEmail(event.currentTarget.value)}
                disabled={report.isPending}
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
              className="rs-primary rs-modal-submit"
              type="submit"
              disabled={!reason || report.isPending}
            >
              {report.isPending ? "Sending…" : "Send report"}
            </button>
          </div>
        </form>
      )}
    </Dialog>
  );
}
