import { Link } from "@tanstack/react-router";
import { IconExternalLink } from "@tabler/icons-react";
import { useState, type FormEvent } from "react";
import {
  formatDate,
  formatDuration,
} from "@/components/Reelshelf/reelshelf-model";
import { Badge, Button, Input } from "@/components/ui";
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

      {run ? <RunResult run={run} /> : <p className="rs-meta">Not run yet.</p>}

      <RerunForm clipId={clip.clipId} />

      <SummaryReview clipId={clip.clipId} />

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
                    {item.transcript ? `${item.transcript.length} chars` : ""}
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
              ? ` (heard nothing; transcript from ${run.fallbackModel})`
              : ` (${run.fallbackModel} heard nothing either)`
            : ""}
          {run.audioSeconds !== null
            ? ` · ${Math.round(run.audioSeconds)}s of audio`
            : ""}
          {run.languages.length > 0 ? ` · ${run.languages.join(", ")}` : ""}
          {run.durationMs !== null
            ? ` · took ${(run.durationMs / 1000).toFixed(1)}s`
            : ""}
        </span>
      </div>

      {outcome === "transcribed" ? (
        <blockquote className="rs-transcript">{run.transcript}</blockquote>
      ) : outcome === "silent" ? (
        <p className="rs-meta">The clip has audio, but no words came back.</p>
      ) : outcome === "no-audio" ? (
        <p className="rs-meta">
          The clip has no audio track, so nothing was sent.
        </p>
      ) : outcome === "failed" ? (
        <p className="rs-legend-error">{run.error}</p>
      ) : (
        <p className="rs-meta">
          {run.status === "pending" && run.error
            ? `Retrying after: ${run.error}`
            : "Waiting for a worker…"}
        </p>
      )}

      {run.prompt ? (
        <details className="rs-legend-raw">
          <summary>
            Prompt {run.promptVersion ? `(${run.promptVersion})` : ""} and{" "}
            {run.keywords.length} keywords
          </summary>
          <pre className="rs-transcript-prompt">
            {run.prompt}
            {run.keywords.length > 0
              ? `\n\nKeywords: ${run.keywords.join(", ")}`
              : ""}
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
