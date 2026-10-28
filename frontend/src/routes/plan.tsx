import { Link, createFileRoute } from "@tanstack/react-router";
import { useState } from "react";
import {
  useExports,
  useOverLimit,
  usePlanOffers,
  useSetClipKept,
  useStartExport,
} from "@/hooks/queries";
import { ApiError } from "@/shared/services/apiError";
import {
  type ClipExport,
  exportPartUrl,
  upgradeKeepsMoreClips,
} from "@/shared/services/plans";
import type { OverLimitClip } from "@/api-client";
import { formatFileSize } from "@/shared/utils/format";
import { planCopy } from "@/copy/plan";

const copy = planCopy.page;

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

function PlanRoute() {
  const overLimit = useOverLimit();
  const exports = useExports();
  const offers = usePlanOffers();
  const data = overLimit.data;
  const over = data?.over ?? [];
  const deletesOn = formatDate(data?.deletesAt);

  return (
    <>
      <section className="rs-hero">
        <div className="rs-eyebrow">{copy.eyebrow}</div>
        <h1 className="rs-display rs-h1">
          {over.length > 0 ? copy.overTitle(over.length) : copy.title}
        </h1>
      </section>
      <section className="rs-section rs-settings">
        {overLimit.isError ? (
          <p className="rs-linked-notice" data-tone="error">
            {copy.loadFailed}
          </p>
        ) : null}

        {data && over.length > 0 ? (
          <div className="rs-settings-section">
            <p className="rs-sidebar-copy">
              {copy.overIntro(
                data.clipLimit,
                over.length,
                deletesOn,
                upgradeKeepsMoreClips(offers.data, data.clipLimit) ? (
                  <Link to="/settings">{copy.upgradeLink}</Link>
                ) : null,
              )}
            </p>
            <StartExportButton exports={exports.data ?? []} />
          </div>
        ) : data ? (
          <p className="rs-sidebar-copy">
            {copy.withinPlan(<Link to="/settings">{copy.settingsLink}</Link>)}
          </p>
        ) : null}

        {(exports.data ?? []).length > 0 ? (
          <div className="rs-settings-section">
            <h2 className="rs-eyebrow">{copy.downloads}</h2>
            <ExportList exports={exports.data ?? []} />
          </div>
        ) : null}

        {over.length > 0 ? (
          <div className="rs-settings-section">
            <h2 className="rs-eyebrow">{copy.overTheLimit}</h2>
            <ClipList clips={over} keep />
          </div>
        ) : null}

        {(data?.picked ?? []).length > 0 ? (
          <div className="rs-settings-section">
            <h2 className="rs-eyebrow">{copy.picked}</h2>
            <p className="rs-sidebar-copy">{copy.pickedCopy}</p>
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
        ? copy.exportFailed
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
          {making ? copy.makingExport : copy.startExport}
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
            ? copy.exports.graceEnded(e.clipCount)
            : copy.exports.overLimit(e.clipCount);
        const until = formatDate(e.expiresAt);
        return (
          <li key={e.id} className="rs-plan-list-row">
            <span className="rs-plan-list-main">
              <span>{what}</span>
              <span className="rs-plan-list-sub">
                {e.status === "ready"
                  ? copy.exports.readyUntil(until)
                  : e.status === "failed"
                    ? copy.exports.failed
                    : e.status === "expired"
                      ? copy.exports.expired
                      : copy.exports.making}
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
                    {e.parts.length > 1 ? copy.exports.part(part.index) : ""}
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
              <span>{clip.title || copy.untitledClip}</span>
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
              {keep ? copy.keepInstead : copy.unpick}
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
          {copy.showAll(clips.length)}
        </button>
      ) : null}
    </>
  );
}
