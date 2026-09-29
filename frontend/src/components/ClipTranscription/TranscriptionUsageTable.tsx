import type { ClipTranscriptionUsage } from "@/shared/services/clipTranscription";
import { formatTokens } from "@/components/LegendDetection/legendReview";
import { formatCost } from "./transcriptionReview";

/** Runs, audio minutes and estimated cost per model and prompt version, covering every owner's clips. */
export function TranscriptionUsageTable({
  usage,
}: {
  usage: ClipTranscriptionUsage[];
}) {
  if (usage.length === 0) return null;

  return (
    <div className="rs-legend-usage">
      <table className="rs-legend-table">
        <thead>
          <tr>
            <th scope="col">Model</th>
            <th scope="col">Prompt</th>
            <th scope="col">Succeeded</th>
            <th scope="col">Failed</th>
            <th scope="col">Queued</th>
            <th scope="col" title="Clips with audio but no words">
              No speech
            </th>
            <th scope="col" title="Clips without an audio track; never sent">
              No audio
            </th>
            <th
              scope="col"
              title="Runs where the model heard nothing and the fallback model was tried; in brackets, how many it found speech in"
            >
              Fallback
            </th>
            <th scope="col">Audio</th>
            <th
              scope="col"
              title="From the model's price in ClipTranscription:CostPerMinuteUsd"
            >
              Est. cost
            </th>
            <th scope="col">Input tokens</th>
            <th scope="col">Output tokens</th>
            <th scope="col">Avg time</th>
          </tr>
        </thead>
        <tbody>
          {usage.map((row) => (
            <tr key={`${row.model}/${row.promptVersion}`}>
              <td>{row.model}</td>
              <td>{row.promptVersion ?? "–"}</td>
              <td>{row.succeeded}</td>
              <td>{row.failed}</td>
              <td>{row.queued}</td>
              <td>{row.withoutSpeech}</td>
              <td>{row.withoutAudio}</td>
              <td>
                {row.fallbackRuns}
                {row.fallbackRuns > 0 ? ` (${row.fallbackRecovered})` : ""}
              </td>
              <td>{row.audioMinutes.toFixed(1)} min</td>
              <td>{formatCost(row.estimatedCostUsd)}</td>
              <td>{formatTokens(row.inputTokens)}</td>
              <td>{formatTokens(row.outputTokens)}</td>
              <td>
                {row.averageDurationMs === null
                  ? "–"
                  : `${(row.averageDurationMs / 1000).toFixed(1)}s`}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
