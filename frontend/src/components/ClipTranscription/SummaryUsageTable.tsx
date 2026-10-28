import { clipTranscriptionCopy } from "@/copy/clipTranscription";
import { runsCopy } from "@/copy/runs";
import type { ClipSummaryUsage } from "@/shared/services/clipSummary";
import {
  cachedShare,
  formatReasoningEffort,
  formatTokens,
} from "@/components/LegendDetection/legendReview";

/** Summary runs and tokens per provider, model, reasoning effort and prompt version. */
export function SummaryUsageTable({ usage }: { usage: ClipSummaryUsage[] }) {
  if (usage.length === 0) return null;

  return (
    <div className="rs-legend-usage">
      <table className="rs-legend-table">
        <thead>
          <tr>
            <th scope="col">{clipTranscriptionCopy.usage.summaryModel}</th>
            <th scope="col">{runsCopy.columns.reasoning}</th>
            <th scope="col">{runsCopy.columns.prompt}</th>
            <th scope="col">{runsCopy.columns.succeeded}</th>
            <th scope="col">{runsCopy.columns.failed}</th>
            <th scope="col">{runsCopy.columns.queued}</th>
            <th scope="col">{runsCopy.columns.inputTokens}</th>
            <th scope="col">{runsCopy.columns.cached}</th>
            <th scope="col">{runsCopy.columns.outputTokens}</th>
            <th scope="col">{clipTranscriptionCopy.usage.embeddingTokens}</th>
            <th scope="col">{runsCopy.columns.avgTime}</th>
          </tr>
        </thead>
        <tbody>
          {usage.map((row) => (
            <tr
              key={`${row.provider}/${row.model}/${row.reasoningEffort}/${row.promptVersion}`}
            >
              <td>
                {row.provider}/{row.model}
              </td>
              <td>{formatReasoningEffort(row.reasoningEffort)}</td>
              <td>{row.promptVersion ?? "–"}</td>
              <td>{row.succeeded}</td>
              <td>{row.failed}</td>
              <td>{row.queued}</td>
              <td>{formatTokens(row.inputTokens)}</td>
              <td>
                {runsCopy.units.percent(Math.round(cachedShare(row) * 100))}
              </td>
              <td>{formatTokens(row.outputTokens)}</td>
              <td>{formatTokens(row.embeddingTokens)}</td>
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
