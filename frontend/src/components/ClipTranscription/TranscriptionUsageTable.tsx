import type { ClipTranscriptionUsage } from "@/shared/services/clipTranscription";
import { formatTokens } from "@/components/LegendDetection/legendReview";
import { clipTranscriptionCopy } from "@/copy/clipTranscription";
import { runsCopy } from "@/copy/runs";
import { formatCost } from "./transcriptionReview";

const copy = clipTranscriptionCopy.usage;

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
            <th scope="col">{runsCopy.columns.model}</th>
            <th scope="col">{runsCopy.columns.prompt}</th>
            <th scope="col">{runsCopy.columns.succeeded}</th>
            <th scope="col">{runsCopy.columns.failed}</th>
            <th scope="col">{runsCopy.columns.queued}</th>
            <th scope="col" title={copy.noSpeechTitle}>
              {copy.noSpeech}
            </th>
            <th scope="col" title={copy.noAudioTitle}>
              {copy.noAudio}
            </th>
            <th scope="col" title={copy.fallbackTitle}>
              {copy.fallback}
            </th>
            <th scope="col">{copy.audio}</th>
            <th scope="col" title={copy.estCostTitle}>
              {copy.estCost}
            </th>
            <th scope="col">{runsCopy.columns.inputTokens}</th>
            <th scope="col">{runsCopy.columns.outputTokens}</th>
            <th scope="col">{runsCopy.columns.avgTime}</th>
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
              <td>{copy.audioMinutes(row.audioMinutes)}</td>
              <td>{formatCost(row.estimatedCostUsd)}</td>
              <td>{formatTokens(row.inputTokens)}</td>
              <td>{formatTokens(row.outputTokens)}</td>
              <td>
                {row.averageDurationMs === null
                  ? "–"
                  : runsCopy.units.durationMs(row.averageDurationMs)}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
