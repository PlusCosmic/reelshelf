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
            <th scope="col">Summary model</th>
            <th scope="col">Reasoning</th>
            <th scope="col">Prompt</th>
            <th scope="col">Succeeded</th>
            <th scope="col">Failed</th>
            <th scope="col">Queued</th>
            <th scope="col">Input tokens</th>
            <th scope="col">Cached</th>
            <th scope="col">Output tokens</th>
            <th scope="col">Embedding tokens</th>
            <th scope="col">Avg time</th>
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
              <td>{Math.round(cachedShare(row) * 100)}%</td>
              <td>{formatTokens(row.outputTokens)}</td>
              <td>{formatTokens(row.embeddingTokens)}</td>
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
