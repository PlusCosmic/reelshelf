import { Link } from "@tanstack/react-router";
import { IconExternalLink } from "@tabler/icons-react";
import { useState, type FormEvent } from "react";
import {
  formatDate,
  formatDuration,
} from "@/components/Reelshelf/reelshelf-model";
import { ClipVideoStage } from "@/components/Video/ClipVideoStage";
import { Badge, Button, Input } from "@/components/ui";
import { clipTranscriptionCopy } from "@/copy/clipTranscription";
import { runsCopy } from "@/copy/runs";
import {
  useQueueTranscriptionRun,
  useTranscriptionRuns,
} from "@/hooks/queries";
import type {
  ClipTranscriptionReviewClip,
  ClipTranscriptionRun,
} from "@/shared/services/clipTranscription";
import { SummaryReview } from "./SummaryReview";
import { transcriptOutcome } from "./transcriptionReview";

const copy = clipTranscriptionCopy.detail;

/** One clip: the full clip to check against, every run's transcript, and its summaries. */
export function TranscriptionReviewDetail({
  clip,
}: {
  clip: ClipTranscriptionReviewClip;
}) {
  const runsQuery = useTranscriptionRuns(clip.clipId);
  const runs =
    runsQuery.data ?? (clip.latestRun === null ? [] : [clip.latestRun]);
  const [chosenRunId, setChosenRunId] = useState<string | null>(null);
  // Newest succeeded run by default, so a queued re-run doesn't hide the transcript being checked.
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
            <span>{clip.gameName}</span>
            <span>{formatDate(clip.createdAt)}</span>
            {clip.lengthSeconds ? (
              <span>{formatDuration(clip.lengthSeconds)}</span>
            ) : null}
          </div>
        </div>
        <Link
          to="/games/$slug/$clipId"
          params={{ slug: clip.gameSlug, clipId: clip.clipId }}
          className="rs-legend-open-clip"
        >
          {runsCopy.openClip} <IconExternalLink size={14} />
        </Link>
      </div>

      <div className="rs-player">
        <ClipVideoStage
          status={clip.videoStatus}
          sources={clip.sources}
          poster={clip.thumbnailUrl}
          title={clip.title}
          className="rs-player-frame"
        />
      </div>

      {run ? (
        <RunResult run={run} />
      ) : (
        <p className="rs-meta">{runsCopy.notRunYet}</p>
      )}

      <RerunForm clipId={clip.clipId} />

      <SummaryReview clipId={clip.clipId} />

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
                  <span>{item.model}</span>
                  <span className="rs-meta">
                    {item.trigger} · {formatDate(item.createdAt)}
                  </span>
                  <span className="rs-legend-run-legend">
                    {item.transcript ? copy.chars(item.transcript.length) : ""}
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

function RunResult({ run }: { run: ClipTranscriptionRun }) {
  const outcome = transcriptOutcome(run);
  return (
    <div className="rs-legend-result">
      <div className="rs-legend-result-heading">
        <Badge tone={statusTone(run.status)}>{run.status}</Badge>
        <span className="rs-meta">
          {run.model}
          {run.fallbackModel
            ? run.transcript
              ? copy.heardNothing(run.fallbackModel)
              : copy.heardNothingEither(run.fallbackModel)
            : ""}
          {run.audioSeconds !== null ? copy.audioSeconds(run.audioSeconds) : ""}
          {run.languages.length > 0 ? ` · ${run.languages.join(", ")}` : ""}
          {run.durationMs !== null ? runsCopy.took(run.durationMs) : ""}
        </span>
      </div>

      {outcome === "transcribed" ? (
        <blockquote className="rs-transcript">{run.transcript}</blockquote>
      ) : outcome === "silent" ? (
        <p className="rs-meta">{copy.silent}</p>
      ) : outcome === "no-audio" ? (
        <p className="rs-meta">{copy.noAudio}</p>
      ) : outcome === "failed" ? (
        <p className="rs-legend-error">{run.error}</p>
      ) : (
        <p className="rs-meta">
          {run.status === "pending" && run.error
            ? runsCopy.retryingAfter(run.error)
            : runsCopy.waitingForWorker}
        </p>
      )}

      {run.prompt ? (
        <details className="rs-legend-raw">
          <summary>
            {copy.promptSummary(run.promptVersion, run.keywords.length)}
          </summary>
          <pre className="rs-transcript-prompt">
            {run.prompt}
            {run.keywords.length > 0 ? copy.keywords(run.keywords) : ""}
          </pre>
        </details>
      ) : null}
    </div>
  );
}

function RerunForm({ clipId }: { clipId: string }) {
  const [model, setModel] = useState("");
  const queue = useQueueTranscriptionRun();

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
        placeholder={runsCopy.rerun.modelPlaceholder}
        aria-label={runsCopy.rerun.modelLabel}
      />
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
