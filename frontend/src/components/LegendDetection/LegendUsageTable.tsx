import type { LegendDetectionUsage } from "@/shared/services/legendDetection";
import { cachedShare, formatTokens } from "./legendReview";

/** Runs and tokens per model and prompt version, covering every user's clips. */
export function LegendUsageTable({ usage }: { usage: LegendDetectionUsage[] }) {
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
            <th scope="col">Input tokens</th>
            <th scope="col">Cached</th>
            <th scope="col">Output tokens</th>
            <th scope="col">Avg time</th>
          </tr>
        </thead>
        <tbody>
          {usage.map((row) => (
            <tr key={`${row.provider}/${row.model}/${row.promptVersion}`}>
              <td>{row.model}</td>
              <td>{row.promptVersion ?? "–"}</td>
              <td>{row.succeeded}</td>
              <td>{row.failed}</td>
              <td>{row.queued}</td>
              <td>{formatTokens(row.inputTokens)}</td>
              <td>{Math.round(cachedShare(row) * 100)}%</td>
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
