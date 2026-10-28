import { createFileRoute, useNavigate } from "@tanstack/react-router";
import { useState } from "react";
import { adminCopy } from "@/copy/admin";
import { commonCopy } from "@/copy/common";
import { formatDate } from "@/components/Reelshelf/reelshelf-model";
import { Badge, Button, Field, Input } from "@/components/ui";
import {
  useContentReports,
  useCurrentUser,
  useResolveContentReport,
} from "@/hooks/queries";
import {
  reportReasonLabel,
  urgentReasons,
  type AdminContentReport,
} from "@/shared/services/contentReports";

type AdminReportsSearch = {
  resolved?: boolean;
};

export const Route = createFileRoute("/admin/reports")({
  component: AdminReportsRoute,
  validateSearch: (search: Record<string, unknown>): AdminReportsSearch => ({
    resolved:
      search.resolved === true || search.resolved === "true" || undefined,
  }),
});

const copy = adminCopy.reports;
const statusNames = copy.statuses;

function AdminReportsRoute() {
  const { data: user, isLoading } = useCurrentUser();

  if (isLoading)
    return <div className="rs-section rs-empty">{commonCopy.loading}</div>;
  if (!user?.isAdmin)
    return <div className="rs-section rs-empty">{copy.adminsOnly}</div>;
  return <AdminReports />;
}

function AdminReports() {
  const search = Route.useSearch();
  const resolved = search.resolved === true;
  const navigate = useNavigate({ from: Route.fullPath });
  const reports = useContentReports(resolved);

  return (
    <div className="rs-legend-page">
      <header className="rs-legend-header">
        <div>
          <div className="rs-eyebrow">{copy.eyebrow}</div>
          <h1 className="rs-display rs-h2">{copy.title(resolved)}</h1>
        </div>
        <Button
          size="sm"
          onClick={() =>
            void navigate({
              search: resolved ? {} : { resolved: true },
              replace: true,
            })
          }
        >
          {copy.toggle(resolved)}
        </Button>
      </header>

      {reports.isError ? (
        <p className="rs-legend-error">{copy.loadFailed}</p>
      ) : !reports.data ? (
        <p className="rs-meta">{commonCopy.loading}</p>
      ) : reports.data.length === 0 ? (
        <p className="rs-meta">{copy.empty(resolved)}</p>
      ) : (
        <ul className="rs-report-list">
          {reports.data.map((report) => (
            <li key={report.id}>
              <ReportCard report={report} />
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function ReportCard({ report }: { report: AdminContentReport }) {
  const urgent = urgentReasons.has(report.reason);

  return (
    <article
      className={`rs-report-card${urgent && report.status === "open" ? " rs-report-card-urgent" : ""}`}
    >
      <div className="rs-meta">
        <Badge tone={urgent ? "danger" : "neutral"}>
          {reportReasonLabel(report.reason)}
        </Badge>
        <Badge
          tone={
            report.status === "taken_down" || report.status === "removed"
              ? "danger"
              : "neutral"
          }
        >
          {statusNames[report.status] ?? report.status}
        </Badge>
        <span>{copy.reported(formatDate(report.createdAt))}</span>
        {report.openReportsOnClip > 1 ? (
          <span>{copy.openReportsOnClip(report.openReportsOnClip)}</span>
        ) : null}
      </div>

      <h2>
        {report.clipTitle ?? copy.untitled}
        {report.ownerName ? (
          <span className="rs-meta">{copy.byOwner(report.ownerName)}</span>
        ) : null}
      </h2>

      <p className="rs-meta">
        {shareState(report)}
        {report.shareUrl ? (
          <a href={report.shareUrl} target="_blank" rel="noreferrer">
            {copy.openSharedClip}
          </a>
        ) : null}
        {report.reporterEmail ? (
          <span>
            {copy.reporter(
              <a href={`mailto:${report.reporterEmail}`}>
                {report.reporterEmail}
              </a>,
            )}
          </span>
        ) : null}
      </p>

      {report.details ? <blockquote>{report.details}</blockquote> : null}

      {report.reason === "child_safety" && report.status === "open" ? (
        <p className="rs-legend-error">
          {copy.childSafetyWarning(report.clipId)}
        </p>
      ) : null}

      {report.status === "open" ? (
        <ResolveReport report={report} urgent={urgent} />
      ) : (
        <>
          <p className="rs-meta">
            {statusNames[report.status]}{" "}
            {report.resolvedAt ? formatDate(report.resolvedAt) : ""}
            {report.resolvedByName
              ? copy.resolvedBy(report.resolvedByName)
              : ""}
            {report.resolutionNote ? `: ${report.resolutionNote}` : ""}
          </p>
          {report.status === "removed" ? (
            <RemovalState report={report} />
          ) : null}
        </>
      )}
    </article>
  );
}

/** What has become of the clip's sharing, unless there's a link to open instead. */
function shareState(report: AdminContentReport): string | null {
  if (report.status === "removed") return copy.shareState.removed;
  if (!report.clipId) return copy.shareState.deleted;
  if (report.sharingBlocked) return copy.shareState.blocked;
  if (report.shareHidden) return copy.shareState.hidden;
  return report.shareUrl ? null : copy.shareState.noLink;
}

/** What became of a removed clip's evidence and its owner's account. */
function RemovalState({ report }: { report: AdminContentReport }) {
  const evidence = report.evidence;
  return (
    <ul className="rs-report-removal">
      {!evidence ? (
        <li>{copy.removal.nothingKept}</li>
      ) : evidence.status === "failed" ? (
        <li className="rs-legend-error">
          {copy.removal.failed(evidence.error)}
        </li>
      ) : evidence.status === "pending" ? (
        <li>
          {copy.removal.pending}
          {evidence.error ? copy.removal.retrying(evidence.error) : ""}
        </li>
      ) : (
        <li>{copy.removal.kept(formatDate(evidence.deleteAfter))}</li>
      )}
      {evidence?.cdnPurge === "not_configured" ? (
        <li className="rs-legend-error">{copy.removal.notPurged}</li>
      ) : null}
      <li>
        {report.ownerSuspended
          ? copy.removal.ownerSuspended
          : copy.removal.ownerNotSuspended}
      </li>
    </ul>
  );
}

function ResolveReport({
  report,
  urgent,
}: {
  report: AdminContentReport;
  urgent: boolean;
}) {
  const [note, setNote] = useState("");
  const resolve = useResolveContentReport();
  const run = (action: "take-down" | "remove" | "dismiss") =>
    resolve.mutate({ reportId: report.id, action, note: note.trim() });

  return (
    <div className="rs-report-actions">
      {urgent ? (
        <p className="rs-meta rs-report-explain">
          {copy.resolve.removeExplain(report.ownerName)}
        </p>
      ) : null}
      <Field label={copy.resolve.note}>
        <Input
          value={note}
          maxLength={500}
          placeholder={copy.resolve.notePlaceholder(urgent)}
          onChange={(event) => setNote(event.target.value)}
        />
      </Field>
      <Button
        variant="primary"
        disabled={!note.trim() || resolve.isPending}
        onClick={() => run(urgent ? "remove" : "take-down")}
      >
        {urgent ? copy.resolve.remove : copy.resolve.takeDown}
      </Button>
      <Button disabled={resolve.isPending} onClick={() => run("dismiss")}>
        {copy.resolve.dismiss}
      </Button>
      {resolve.isError ? (
        <span className="rs-legend-error">{resolve.error.message}</span>
      ) : null}
    </div>
  );
}
