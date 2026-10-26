import { createFileRoute, useNavigate } from "@tanstack/react-router";
import { useState } from "react";
import { formatDate } from "@/components/Reelshelf/reelshelf-model";
import { Badge, Button, Field, Input } from "@/components/ui";
import {
  useContentReports,
  useCurrentUser,
  useResolveContentReport,
} from "@/hooks/queries";
import {
  reportReasonLabel,
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

const urgentReasons = new Set(["child_safety", "terrorism"]);

const statusNames: Record<string, string> = {
  open: "Open",
  taken_down: "Taken down",
  dismissed: "Dismissed",
};

function AdminReportsRoute() {
  const { data: user, isLoading } = useCurrentUser();

  if (isLoading) return <div className="rs-section rs-empty">Loading…</div>;
  if (!user?.isAdmin)
    return (
      <div className="rs-section rs-empty">
        Reports are only available to admins.
      </div>
    );
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
          <div className="rs-eyebrow">Reports</div>
          <h1 className="rs-display rs-h2">
            {resolved ? "Reports dealt with." : "Reported shared clips."}
          </h1>
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
          {resolved ? "Show open reports" : "Show dealt with"}
        </Button>
      </header>

      {reports.isError ? (
        <p className="rs-legend-error">The reports couldn't be loaded.</p>
      ) : !reports.data ? (
        <p className="rs-meta">Loading…</p>
      ) : reports.data.length === 0 ? (
        <p className="rs-meta">
          {resolved ? "No report has been dealt with yet." : "No open reports."}
        </p>
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
        <Badge tone={report.status === "taken_down" ? "danger" : "neutral"}>
          {statusNames[report.status] ?? report.status}
        </Badge>
        <span>Reported {formatDate(report.createdAt)}</span>
        {report.openReportsOnClip > 1 ? (
          <span>{report.openReportsOnClip} open reports on this clip</span>
        ) : null}
      </div>

      <h2>
        {report.clipTitle ?? "Untitled clip"}
        {report.ownerName ? (
          <span className="rs-meta"> by {report.ownerName}</span>
        ) : null}
      </h2>

      <p className="rs-meta">
        {shareState(report)}
        {report.shareUrl ? (
          <a href={report.shareUrl} target="_blank" rel="noreferrer">
            Open the shared clip
          </a>
        ) : null}
        {report.reporterEmail ? (
          <span>
            Reporter:{" "}
            <a href={`mailto:${report.reporterEmail}`}>
              {report.reporterEmail}
            </a>
          </span>
        ) : null}
      </p>

      {report.details ? <blockquote>{report.details}</blockquote> : null}

      {report.reason === "child_safety" && report.status === "open" ? (
        <p className="rs-legend-error">
          Don't open or download this clip. If the report looks genuine, take it
          down, keep the clip, and report it to the IWF (UK) or NCMEC's
          CyberTipline (US) with the clip's id ({report.clipId ?? "deleted"}).
          Its share link stays hidden until you decide.
        </p>
      ) : null}

      {report.status === "open" ? (
        <ResolveReport report={report} />
      ) : (
        <p className="rs-meta">
          {statusNames[report.status]}{" "}
          {report.resolvedAt ? formatDate(report.resolvedAt) : ""}
          {report.resolvedByName ? ` by ${report.resolvedByName}` : ""}
          {report.resolutionNote ? `: ${report.resolutionNote}` : ""}
        </p>
      )}
    </article>
  );
}

/** What has become of the clip's sharing, unless there's a link to open instead. */
function shareState(report: AdminContentReport): string | null {
  if (!report.clipId) return "The clip has since been deleted.";
  if (report.sharingBlocked) return "Taken down: it can't be shared.";
  if (report.shareHidden)
    return "Its share link is hidden while a child-safety or terrorism report is open.";
  return report.shareUrl ? null : "It has no live share link.";
}

function ResolveReport({ report }: { report: AdminContentReport }) {
  const [note, setNote] = useState("");
  const resolve = useResolveContentReport();
  const run = (action: "take-down" | "dismiss") =>
    resolve.mutate({ reportId: report.id, action, note: note.trim() });

  return (
    <div className="rs-report-actions">
      <Field label="Note">
        <Input
          value={note}
          maxLength={500}
          placeholder="Why, for the record (needed to take down)"
          onChange={(event) => setNote(event.target.value)}
        />
      </Field>
      <Button
        variant="primary"
        disabled={!note.trim() || resolve.isPending}
        onClick={() => run("take-down")}
      >
        Take down
      </Button>
      <Button disabled={resolve.isPending} onClick={() => run("dismiss")}>
        Dismiss
      </Button>
      {resolve.isError ? (
        <span className="rs-legend-error">{resolve.error.message}</span>
      ) : null}
    </div>
  );
}
