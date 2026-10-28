import { Link } from "@tanstack/react-router";
import { IconExternalLink } from "@tabler/icons-react";
import { useState, type FormEvent } from "react";
import {
  formatDate,
  formatDuration,
} from "@/components/Reelshelf/reelshelf-model";
import { ClipVideoStage } from "@/components/Video/ClipVideoStage";
import { Badge, Button } from "@/components/ui";
import { useLegendRuns, useQueueLegendRun } from "@/hooks/queries";
import { commonCopy } from "@/copy/common";
import { legendDetectionCopy } from "@/copy/legendDetection";
import { runsCopy } from "@/copy/runs";
import { LegendLabelEditor } from "./LegendLabelEditor";
import type {
  LegendDetectionReviewClip,
  LegendDetectionRun,
} from "@/shared/services/legendDetection";
import {
  confidenceTone,
  formatConfidence,
  formatReasoningEffort,
  formatTokens,
} from "./legendReview";
import { useRunOptions } from "./RunOptionsFields";

const copy = legendDetectionCopy.detail;

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
          {runsCopy.openClip} <IconExternalLink size={14} />
        </Link>
      </div>

      <div className="rs-player">
        <ClipVideoStage
          status={clip.videoStatus}
          sources={clip.sources}
          poster={clip.frameUrls[0]}
          title={clip.title}
          className="rs-player-frame"
        />
      </div>

      <h3 className="rs-eyebrow">{copy.framesHeading}</h3>
      <div className="rs-legend-frames">
        {clip.frameUrls.map((url, index) => (
          <a
            key={url}
            href={url}
            target="_blank"
            rel="noreferrer"
            className="rs-legend-frame"
            title={copy.screenshot(index + 1)}
          >
            <img
              src={url}
              alt={copy.screenshot(index + 1)}
              loading="lazy"
              onError={(event) =>
                event.currentTarget.parentElement?.classList.add("is-missing")
              }
            />
            <span>{index + 1}</span>
          </a>
        ))}
      </div>

      {run ? (
        <RunResult run={run} />
      ) : (
        <p className="rs-meta">{runsCopy.notRunYet}</p>
      )}

      {/* Re-keyed when the label or shown run changes, so the fields start from the latest values. */}
      <LegendLabelEditor
        key={`${clip.label?.labelledAt.toISOString() ?? "unlabelled"}/${run?.id ?? "none"}`}
        clip={clip}
        run={run}
      />

      <RerunForm clipId={clip.clipId} />

      {runs.length > 0 ? (
        <>
          <h3 className="rs-eyebrow">{runsCopy.runsHeading}</h3>
          <ol className="rs-legend-runs">
            {runs.map((item) => (
              <li key={item.id}>
                <button
                  type="button"
                  className={`rs-legend-run${item.id === run?.id ? " selected" : ""}`}
                  onClick={() => setChosenRunId(item.id)}
                >
                  <Badge tone={statusTone(item.status)}>{item.status}</Badge>
                  <span>
                    {item.model} · {formatReasoningEffort(item.reasoningEffort)}
                  </span>
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
      label: copy.you,
      legend: run.playerLegend,
      legendConfidence: run.playerLegendConfidence,
      name: run.playerName,
      nameConfidence: run.playerNameConfidence,
    },
    ...[...(run.teammates ?? [])]
      .sort((a, b) => a.slot - b.slot)
      .map((teammate) => ({
        label: copy.teammate(teammate.slot),
        legend: teammate.legend,
        legendConfidence: teammate.legendConfidence,
        name: teammate.name,
        nameConfidence: teammate.nameConfidence,
      })),
  ];

  return (
    <div className="rs-legend-result">
      <div className="rs-legend-result-heading">
        <h3 className="rs-eyebrow">{copy.detected}</h3>
        <Badge tone={statusTone(run.status)}>{run.status}</Badge>
        {run.status === "succeeded" && !run.hudDetected ? (
          <Badge tone="danger">{copy.hudNotFound}</Badge>
        ) : null}
      </div>

      {run.status === "succeeded" ? (
        <table className="rs-legend-table">
          <thead>
            <tr>
              <th scope="col" />
              <th scope="col">{copy.legend}</th>
              <th scope="col">{copy.name}</th>
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
          <dt>{runsCopy.columns.model}</dt>
          <dd>
            {run.provider} / {run.model}
          </dd>
        </div>
        <div>
          <dt>{runsCopy.columns.reasoning}</dt>
          <dd>{formatReasoningEffort(run.reasoningEffort)}</dd>
        </div>
        <div>
          <dt>{runsCopy.columns.prompt}</dt>
          <dd>{run.promptVersion ?? "–"}</dd>
        </div>
        <div>
          <dt>{runsCopy.columns.inputTokens}</dt>
          <dd>
            {formatTokens(run.inputTokens)}
            {run.cachedInputTokens
              ? copy.cachedTokens(formatTokens(run.cachedInputTokens))
              : ""}
          </dd>
        </div>
        <div>
          <dt>{runsCopy.columns.outputTokens}</dt>
          <dd>{formatTokens(run.outputTokens)}</dd>
        </div>
        <div>
          <dt>{copy.time}</dt>
          <dd>
            {run.durationMs === null
              ? "–"
              : commonCopy.seconds(run.durationMs / 1000)}
          </dd>
        </div>
        <div>
          <dt>{copy.framesAttempts}</dt>
          <dd>
            {run.frameCount ?? "–"} · {run.attempts}
          </dd>
        </div>
      </dl>

      {run.rawResponse ? (
        <details className="rs-legend-raw">
          <summary>{runsCopy.rawResponse}</summary>
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
  if (value === null)
    return <span className="rs-meta">{copy.notIdentified}</span>;
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
  const { options, fields } = useRunOptions();
  const queue = useQueueLegendRun();

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    queue.mutate({ clipId, ...options });
  }

  return (
    <form className="rs-legend-rerun" onSubmit={onSubmit}>
      {fields}
      <Button type="submit" size="sm" disabled={queue.isPending}>
        {queue.isPending ? runsCopy.rerun.queueing : runsCopy.rerun.runAgain}
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
