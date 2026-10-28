import { useState } from "react";
import type { FormEvent } from "react";
import { IconFlag } from "@tabler/icons-react";
import { Dialog } from "@/components/Reelshelf/Dialog";
import { commonCopy } from "@/copy/common";
import { shareCopy } from "@/copy/share";
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
          {shareCopy.report.button}
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
              : shareCopy.report.failed,
          );
        },
      },
    );
  };

  if (report.isSuccess) {
    return (
      <Dialog
        eyebrow={shareCopy.report.eyebrow}
        title={shareCopy.report.thanks.title}
        onClose={onClose}
      >
        {() => (
          <div className="rs-modal-panel">
            <p className="rs-modal-note">
              {shareCopy.report.thanks.note}
              {reason === "child_safety" || reason === "terrorism"
                ? shareCopy.report.thanks.linkHidden
                : ""}
            </p>
            <div className="rs-modal-actions">
              <button
                className="rs-primary rs-modal-submit"
                type="button"
                onClick={onClose}
              >
                {commonCopy.close}
              </button>
            </div>
          </div>
        )}
      </Dialog>
    );
  }

  return (
    <Dialog
      eyebrow={shareCopy.report.eyebrow}
      title={shareCopy.report.title}
      busy={report.isPending}
      onClose={onClose}
    >
      {() => (
        <form className="rs-modal-panel rs-report-panel" onSubmit={submit}>
          <fieldset className="rs-report-reasons" disabled={report.isPending}>
            <legend className="rs-visually-hidden">
              {shareCopy.report.reason}
            </legend>
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
              {shareCopy.report.childSafety(
                <a
                  href="https://report.iwf.org.uk/"
                  target="_blank"
                  rel="noreferrer"
                >
                  {shareCopy.report.iwf}
                </a>,
                <a
                  href="https://report.cybertip.org/"
                  target="_blank"
                  rel="noreferrer"
                >
                  {shareCopy.report.ncmec}
                </a>,
              )}
            </p>
          ) : null}
          <label className="rs-field">
            <span>{shareCopy.report.details}</span>
            <textarea
              className="rs-report-details"
              value={details}
              maxLength={2000}
              rows={2}
              onChange={(event) => setDetails(event.currentTarget.value)}
              disabled={report.isPending}
            />
          </label>
          <label className="rs-field">
            <span>
              {shareCopy.report.email}
              {reason === "copyright" ? shareCopy.report.emailCopyright : ""}
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
              {report.isPending
                ? shareCopy.report.sending
                : shareCopy.report.send}
            </button>
          </div>
        </form>
      )}
    </Dialog>
  );
}
