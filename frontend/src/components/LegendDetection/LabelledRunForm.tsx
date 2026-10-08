import type { FormEvent } from "react";
import { Button } from "@/components/ui";
import { useQueueLabelledLegendRuns } from "@/hooks/queries";
import { useRunOptions } from "./RunOptionsFields";

/** Queues one run on every labelled clip, so a provider or model can be scored on the usage table. */
export function LabelledRunForm() {
  const { options, fields } = useRunOptions();
  const queue = useQueueLabelledLegendRuns();

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    const confirmed = window.confirm(
      "Queue a detection run on every labelled clip? Each run is a paid model call.",
    );
    if (confirmed) queue.mutate(options);
  }

  return (
    <form className="rs-legend-rerun" onSubmit={onSubmit}>
      {fields}
      <Button type="submit" size="sm" disabled={queue.isPending}>
        {queue.isPending ? "Queueing…" : "Run on labelled clips"}
      </Button>
      {queue.isSuccess ? (
        <span className="rs-meta">
          Queued {queue.data} {queue.data === 1 ? "run" : "runs"}.
        </span>
      ) : null}
      {queue.isError ? (
        <span className="rs-legend-error">{queue.error.message}</span>
      ) : null}
    </form>
  );
}
