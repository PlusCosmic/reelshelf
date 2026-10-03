import { useState, type FormEvent } from "react";
import { formatDate } from "@/components/Reelshelf/reelshelf-model";
import { ReasoningEffortSelect } from "@/components/LegendDetection/ReasoningEffortSelect";
import { formatReasoningEffort } from "@/components/LegendDetection/legendReview";
import { Badge, Button, Input } from "@/components/ui";
import { useQueueSummaryRun, useSummaryRuns } from "@/hooks/queries";
import type { ClipSummaryRun } from "@/shared/services/clipSummary";

/** A clip's summaries, shown under its transcript: the chosen run's result, a re-run form and every run. */
export function SummaryReview({ clipId }: { clipId: string }) {
  const runsQuery = useSummaryRuns(clipId);
  const runs = runsQuery.data ?? [];
  const [chosenRunId, setChosenRunId] = useState<string | null>(null);
  // Newest succeeded run by default, so a queued re-run doesn't hide the summary being checked.
  const run =
    runs.find((item) => item.id === chosenRunId) ??
    runs.find((item) => item.status === "succeeded") ??
    runs[0];

  return (
    <>
      <h3 className="rs-eyebrow">Summary</h3>
      {runsQuery.isError ? (
        <p className="rs-legend-error">Summaries could not be loaded.</p>
      ) : run ? (
        <SummaryResult run={run} />
      ) : (
        <p className="rs-meta">
          {runsQuery.isLoading ? "Loading…" : "Not summarised yet."}
        </p>
      )}

      <SummaryRerunForm clipId={clipId} />

      {runs.length > 1 ? (
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
                  {item.provider}/{item.model}
                </span>
                <span className="rs-meta">
                  {item.trigger} · {formatReasoningEffort(item.reasoningEffort)}{" "}
                  · {formatDate(item.createdAt)}
                </span>
                <span className="rs-legend-run-legend">
                  {item.moodTags.join(", ")}
                </span>
              </button>
            </li>
          ))}
        </ol>
      ) : null}
    </>
  );
}

function SummaryResult({ run }: { run: ClipSummaryRun }) {
  return (
    <div className="rs-legend-result">
      <div className="rs-legend-result-heading">
        <Badge tone={statusTone(run.status)}>{run.status}</Badge>
        <span className="rs-meta">
          {run.provider}/{run.model} ·{" "}
          {formatReasoningEffort(run.reasoningEffort)} reasoning
          {run.promptVersion ? ` · prompt ${run.promptVersion}` : ""}
          {run.durationMs !== null
            ? ` · took ${(run.durationMs / 1000).toFixed(1)}s`
            : ""}
        </span>
      </div>

      {run.status === "succeeded" ? (
        <>
          <p className="rs-summary-description">{run.description}</p>
          {run.moodTags.length > 0 ? (
            <div className="rs-summary-tags">
              {run.moodTags.map((tag) => (
                <Badge key={tag}>{tag}</Badge>
              ))}
            </div>
          ) : (
            <p className="rs-meta">No mood tags.</p>
          )}
          {run.quotes.length > 0 ? (
            <ul className="rs-summary-quotes">
              {run.quotes.map((quote) => (
                <li key={quote}>“{quote}”</li>
              ))}
            </ul>
          ) : null}
          <p className="rs-meta">
            People:{" "}
            {run.people.length > 0 ? run.people.join(", ") : "none mentioned"}
            {" · "}
            {run.hasEmbedding
              ? `embedded with ${run.embeddingModel}`
              : "no embedding"}
          </p>
        </>
      ) : run.status === "failed" ? (
        <p className="rs-legend-error">{run.error}</p>
      ) : (
        <p className="rs-meta">
          {run.status === "pending" && run.error
            ? `Retrying after: ${run.error}`
            : "Waiting for a worker…"}
        </p>
      )}

      {run.rawResponse ? (
        <details className="rs-legend-raw">
          <summary>Raw response</summary>
          <pre className="rs-transcript-prompt">{run.rawResponse}</pre>
        </details>
      ) : null}
    </div>
  );
}

function SummaryRerunForm({ clipId }: { clipId: string }) {
  const [provider, setProvider] = useState("");
  const [model, setModel] = useState("");
  const [reasoningEffort, setReasoningEffort] = useState("");
  const queue = useQueueSummaryRun();

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    queue.mutate({
      clipId,
      provider: provider.trim() || null,
      model: model.trim() || null,
      reasoningEffort: reasoningEffort || null,
    });
  }

  return (
    <form className="rs-legend-rerun" onSubmit={onSubmit}>
      <Input
        compact
        value={provider}
        onChange={(event) => setProvider(event.target.value)}
        placeholder="Provider (default if empty)"
        aria-label="Summary provider"
      />
      <Input
        compact
        value={model}
        onChange={(event) => setModel(event.target.value)}
        placeholder="Model (default if empty)"
        aria-label="Summary model"
      />
      <ReasoningEffortSelect
        value={reasoningEffort}
        onValueChange={setReasoningEffort}
      />
      <Button type="submit" size="sm" disabled={queue.isPending}>
        {queue.isPending ? "Queueing…" : "Summarise again"}
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
