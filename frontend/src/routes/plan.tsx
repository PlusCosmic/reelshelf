import { Link, createFileRoute } from "@tanstack/react-router";
import { useState } from "react";
import {
  useExports,
  useOverLimit,
  useSetClipKept,
  useStartExport,
} from "@/hooks/queries";
import { ApiError } from "@/shared/services/apiError";
import { type ClipExport, exportPartUrl } from "@/shared/services/plans";
import type { OverLimitClip } from "@/api-client";
import { formatFileSize } from "@/shared/utils/format";

export const Route = createFileRoute("/plan")({
  component: PlanRoute,
});

/** Long lists show this many rows until asked for the rest. */
const shownAtFirst = 100;

function formatDate(value: Date | null | undefined): string | null {
  if (!value) return null;
  return new Date(value).toLocaleDateString(undefined, {
    day: "numeric",
    month: "long",
    year: "numeric",
  });
}

function clipCount(count: number): string {
  return count === 1 ? "1 clip" : `${count.toLocaleString()} clips`;
}

function PlanRoute() {
  const overLimit = useOverLimit();
  const exports = useExports();
  const data = overLimit.data;
  const over = data?.over ?? [];
  const deletesOn = formatDate(data?.deletesAt);

  return (
    <>
      <section className="rs-hero">
        <div className="rs-eyebrow">Plan</div>
        <h1 className="rs-display rs-h1">
          {over.length > 0 ? (
            <>
              <em>{clipCount(over.length)}</em> over your plan's limit.
            </>
          ) : (
            "Your clips and downloads."
          )}
        </h1>
      </section>
      <section className="rs-section rs-settings">
        {overLimit.isError ? (
          <p className="rs-linked-notice" data-tone="error">
            Your clips couldn't be loaded. Refresh to try again.
          </p>
        ) : null}

        {data && over.length > 0 ? (
          <div className="rs-settings-section">
            <p className="rs-sidebar-copy">
              Your plan keeps {data.clipLimit?.toLocaleString()} clips.{" "}
              {deletesOn
                ? `The ${clipCount(over.length)} below will be deleted on ${deletesOn}, and you'll be emailed a download of them.`
                : "The clips below are over the limit; you can't add more until you're back within it."}{" "}
              To keep them, <Link to="/settings">upgrade in Settings</Link>,
              download them, or pick other clips to let go instead.
            </p>
            <StartExportButton exports={exports.data ?? []} />
          </div>
        ) : data ? (
          <p className="rs-sidebar-copy">
            All your clips are within your plan.{" "}
            <Link to="/settings">See your plan in Settings</Link>.
          </p>
        ) : null}

        {(exports.data ?? []).length > 0 ? (
          <div className="rs-settings-section">
            <h2 className="rs-eyebrow">Downloads</h2>
            <ExportList exports={exports.data ?? []} />
          </div>
        ) : null}

        {over.length > 0 ? (
          <div className="rs-settings-section">
            <h2 className="rs-eyebrow">Over the limit</h2>
            <ClipList clips={over} keep />
          </div>
        ) : null}

        {(data?.picked ?? []).length > 0 ? (
          <div className="rs-settings-section">
            <h2 className="rs-eyebrow">Picked to keep</h2>
            <p className="rs-sidebar-copy">
              Clips you picked are kept before your newest ones.
            </p>
            <ClipList clips={data?.picked ?? []} keep={false} />
          </div>
        ) : null}
      </section>
    </>
  );
}

function StartExportButton({ exports }: { exports: ClipExport[] }) {
  const start = useStartExport();
  const making = exports.some(
    (e) => e.status === "pending" || e.status === "running",
  );
  const error =
    start.error instanceof ApiError
      ? start.error.message
      : start.error
        ? "The download couldn't be started. Try again."
        : null;

  return (
    <>
      <div className="rs-email-actions">
        <button
          className="rs-primary"
          type="button"
          disabled={making || start.isPending}
          onClick={() => start.mutate()}
        >
          {making ? "Making your download…" : "Download clips over the limit"}
        </button>
      </div>
      {error ? (
        <p className="rs-linked-notice" data-tone="error">
          {error}
        </p>
      ) : null}
    </>
  );
}

function ExportList({ exports }: { exports: ClipExport[] }) {
  return (
    <ul className="rs-plan-list">
      {exports.map((e) => {
        const what =
          e.reason === "grace_ended"
            ? `${clipCount(e.clipCount)} deleted when your grace ended`
            : `${clipCount(e.clipCount)} over the limit`;
        const until = formatDate(e.expiresAt);
        return (
          <li key={e.id} className="rs-plan-list-row">
            <span className="rs-plan-list-main">
              <span>{what}</span>
              <span className="rs-plan-list-sub">
                {e.status === "ready"
                  ? `Ready until ${until}`
                  : e.status === "failed"
                    ? "Couldn't be made. Email us and we'll sort it out."
                    : e.status === "expired"
                      ? "Expired"
                      : "Being made; this page updates when it's ready"}
              </span>
            </span>
            {e.status === "ready" ? (
              <span className="rs-plan-list-actions">
                {e.parts.map((part) => (
                  <a
                    key={part.index}
                    className="rs-secondary"
                    href={exportPartUrl(e.id, part.index)}
                  >
                    {e.parts.length > 1 ? `Part ${part.index} · ` : ""}
                    {formatFileSize(part.sizeBytes)}
                  </a>
                ))}
              </span>
            ) : null}
          </li>
        );
      })}
    </ul>
  );
}

function ClipList({ clips, keep }: { clips: OverLimitClip[]; keep: boolean }) {
  const [showAll, setShowAll] = useState(false);
  const setKept = useSetClipKept();
  const shown = showAll ? clips : clips.slice(0, shownAtFirst);
  const error =
    setKept.error instanceof ApiError ? setKept.error.message : null;

  return (
    <>
      {error ? (
        <p className="rs-linked-notice" data-tone="error">
          {error}
        </p>
      ) : null}
      <ul className="rs-plan-list">
        {shown.map((clip) => (
          <li key={clip.id} className="rs-plan-list-row">
            <span className="rs-plan-list-main">
              <span>{clip.title || "Untitled clip"}</span>
              <span className="rs-plan-list-sub">
                {[clip.gameName, formatDate(clip.createdAt)]
                  .filter(Boolean)
                  .join(" · ")}
              </span>
            </span>
            <button
              className="rs-small-button"
              type="button"
              disabled={setKept.isPending}
              onClick={() => setKept.mutate({ clipId: clip.id, keep })}
            >
              {keep ? "Keep instead" : "Unpick"}
            </button>
          </li>
        ))}
      </ul>
      {clips.length > shown.length ? (
        <button
          className="rs-small-button"
          type="button"
          onClick={() => setShowAll(true)}
        >
          Show all {clips.length.toLocaleString()}
        </button>
      ) : null}
    </>
  );
}
