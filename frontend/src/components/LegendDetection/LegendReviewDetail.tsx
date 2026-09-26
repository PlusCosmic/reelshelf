import { Link } from "@tanstack/react-router";
import { IconExternalLink } from "@tabler/icons-react";
import { useState, type FormEvent } from "react";
import {
  formatDate,
  formatDuration,
} from "@/components/Reelshelf/reelshelf-model";
import { Badge, Button, Input } from "@/components/ui";
import { useLegendRuns, useQueueLegendRun } from "@/hooks/queries";
import { LegendLabelEditor } from "./LegendLabelEditor";
import type {
  LegendDetectionReviewClip,
  LegendDetectionRun,
} from "@/shared/services/legendDetection";
import { confidenceTone, formatConfidence, formatTokens } from "./legendReview";

/** One clip: the frames the model is sent, the full clip to check against, and every run's answer. */
export function LegendReviewDetail({
  clip,
}: {
  clip: LegendDetectionReviewClip;
}) {
  const runsQuery = useLegendRuns(clip.clipId);
  const runs =
    runsQuery.data ?? (clip.latestRun === null ? [] : [clip.latestRun]);
  const [chosenRunId, setChosenRunId] = useState<string | null>(null);
  // Newest succeeded run by default, so a queued re-run doesn't hide the answer being checked.
  const run =
    runs.find((item) => item.id === chosenRunId) ??
    runs.find((item) => item.status === "succeeded") ??
    runs[0];

  return (
    <section className="rs-legend-detail">
      <div className="rs-legend-detail-heading">
        <div>
          <h2 className="rs-display rs-legend-title">{clip.title}</h2>
          <div className="rs-meta">
            <span>{formatDate(clip.createdAt)}</span>
            {clip.lengthSeconds ? (
              <span>{formatDuration(clip.lengthSeconds)}</span>
            ) : null}
          </div>
        </div>
        <Link
          to="/games/$slug/$clipId"
          params={{ slug: "apex-legends", clipId: clip.clipId }}
          className="rs-legend-open-clip"
        >
          Open clip <IconExternalLink size={14} />
        </Link>
      </div>

      <div className="rs-player">
        <iframe
          src={clip.embedUrl}
          loading="lazy"
          title={clip.title}
          className="rs-player-frame"
          allow="accelerometer; gyroscope; autoplay; encrypted-media; picture-in-picture; fullscreen;"
          allowFullScreen
        />
      </div>

      <h3 className="rs-eyebrow">Frames sent to the model</h3>
      <div className="rs-legend-frames">
        {clip.frameUrls.map((url, index) => (
          <a
            key={url}
            href={url}
            target="_blank"
            rel="noreferrer"
            className="rs-legend-frame"
            title={`Screenshot ${index + 1}`}
          >
            <img
              src={url}
              alt={`Screenshot ${index + 1}`}
              loading="lazy"
              onError={(event) =>
                event.currentTarget.parentElement?.classList.add("is-missing")
              }
            />
            <span>{index + 1}</span>
          </a>
        ))}
      </div>

      {run ? <RunResult run={run} /> : <p className="rs-meta">Not run yet.</p>}

      {/* Re-keyed when the label or shown run changes, so the fields start from the latest values. */}
      <LegendLabelEditor
        key={`${clip.label?.labelledAt.toISOString() ?? "unlabelled"}/${run?.id ?? "none"}`}
        clip={clip}
        run={run}
      />

      <RerunForm clipId={clip.clipId} />

      {runs.length > 0 ? (
        <>
          <h3 className="rs-eyebrow">Runs</h3>
          <ol className="rs-legend-runs">
            {runs.map((item) => (
              <li key={item.id}>
                <button
                  type="button"
                  className={`rs-legend-run${item.id === run?.id ? " selected" : ""}`}
                  onClick={() => setChosenRunId(item.id)}
                >
                  <Badge tone={statusTone(item.status)}>{item.status}</Badge>
                  <span>{item.model}</span>
                  <span className="rs-meta">
                    {item.trigger} · {formatDate(item.createdAt)}
                  </span>
                  <span className="rs-legend-run-legend">
                    {item.playerLegend ?? ""}
                  </span>
                </button>
              </li>
            ))}
          </ol>
        </>
      ) : null}
    </section>
  );
}

function RunResult({ run }: { run: LegendDetectionRun }) {
  const rows = [
    {
      label: "You",
      legend: run.playerLegend,
      legendConfidence: run.playerLegendConfidence,
      name: run.playerName,
      nameConfidence: run.playerNameConfidence,
    },
    ...[...run.teammates]
      .sort((a, b) => a.slot - b.slot)
      .map((teammate) => ({
        label: `Teammate ${teammate.slot}`,
        legend: teammate.legend,
        legendConfidence: teammate.legendConfidence,
        name: teammate.name,
        nameConfidence: teammate.nameConfidence,
      })),
  ];

  return (
    <div className="rs-legend-result">
      <div className="rs-legend-result-heading">
        <h3 className="rs-eyebrow">Detected</h3>
        <Badge tone={statusTone(run.status)}>{run.status}</Badge>
        {run.status === "succeeded" && !run.hudDetected ? (
          <Badge tone="danger">HUD not found</Badge>
        ) : null}
      </div>

      {run.status === "succeeded" ? (
        <table className="rs-legend-table">
          <thead>
            <tr>
              <th scope="col" />
              <th scope="col">Legend</th>
              <th scope="col">Name</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.label}>
                <th scope="row">{row.label}</th>
                <td>
                  <Detected
                    value={row.legend}
                    confidence={row.legendConfidence}
                  />
                </td>
                <td>
                  <Detected value={row.name} confidence={row.nameConfidence} />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : null}

      {run.error ? <p className="rs-legend-error">{run.error}</p> : null}

      <dl className="rs-legend-run-meta">
        <div>
          <dt>Model</dt>
          <dd>
            {run.provider} / {run.model}
          </dd>
        </div>
        <div>
          <dt>Prompt</dt>
          <dd>{run.promptVersion ?? "–"}</dd>
        </div>
        <div>
          <dt>Input tokens</dt>
          <dd>
            {formatTokens(run.inputTokens)}
            {run.cachedInputTokens
              ? ` (${formatTokens(run.cachedInputTokens)} cached)`
              : ""}
          </dd>
        </div>
        <div>
          <dt>Output tokens</dt>
          <dd>{formatTokens(run.outputTokens)}</dd>
        </div>
        <div>
          <dt>Time</dt>
          <dd>
            {run.durationMs === null
              ? "–"
              : `${(run.durationMs / 1000).toFixed(1)}s`}
          </dd>
        </div>
        <div>
          <dt>Frames · attempts</dt>
          <dd>
            {run.frameCount ?? "–"} · {run.attempts}
          </dd>
        </div>
      </dl>

      {run.rawResponse ? (
        <details className="rs-legend-raw">
          <summary>Raw response</summary>
          <pre>{prettyJson(run.rawResponse)}</pre>
        </details>
      ) : null}
    </div>
  );
}

function Detected({
  value,
  confidence,
}: {
  value: string | null;
  confidence: number | null;
}) {
  if (value === null) return <span className="rs-meta">Not identified</span>;
  return (
    <span className="rs-legend-detected">
      {value}
      <Badge tone={confidenceTone(confidence)}>
        {formatConfidence(confidence)}
      </Badge>
    </span>
  );
}

function RerunForm({ clipId }: { clipId: string }) {
  const [model, setModel] = useState("");
  const queue = useQueueLegendRun();

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    queue.mutate({ clipId, model: model.trim() || null });
  }

  return (
    <form className="rs-legend-rerun" onSubmit={onSubmit}>
      <Input
        compact
        value={model}
        onChange={(event) => setModel(event.target.value)}
        placeholder="Model (default if empty)"
        aria-label="Model to run"
      />
      <Button type="submit" size="sm" disabled={queue.isPending}>
        {queue.isPending ? "Queueing…" : "Run again"}
      </Button>
      {queue.isError ? (
        <span className="rs-legend-error">{queue.error.message}</span>
      ) : null}
    </form>
  );
}

function statusTone(status: string) {
  if (status === "succeeded") return "accent";
  return status === "failed" ? "danger" : "neutral";
}

function prettyJson(raw: string) {
  try {
    return JSON.stringify(JSON.parse(raw), null, 2);
  } catch {
    return raw;
  }
}
