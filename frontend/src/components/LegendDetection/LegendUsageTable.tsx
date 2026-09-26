import type { LegendDetectionUsage } from "@/shared/services/legendDetection";
import { cachedShare, formatAccuracy, formatTokens } from "./legendReview";

const archivedFormatter = new Intl.DateTimeFormat(undefined, {
  month: "short",
  day: "numeric",
  hour: "numeric",
  minute: "2-digit",
});

/**
 * Runs and tokens per model and prompt version, covering every user's clips. Each "Start fresh" leaves its
 * runs as an archived batch, so backfills can be compared.
 */
export function LegendUsageTable({ usage }: { usage: LegendDetectionUsage[] }) {
  if (usage.length === 0) return null;

  return (
    <div className="rs-legend-usage">
      <table className="rs-legend-table">
        <thead>
          <tr>
            <th scope="col">Batch</th>
            <th scope="col">Model</th>
            <th scope="col">Prompt</th>
            <th scope="col">Succeeded</th>
            <th scope="col">Failed</th>
            <th scope="col">Queued</th>
            <th scope="col">Input tokens</th>
            <th scope="col">Cached</th>
            <th scope="col">Output tokens</th>
            <th scope="col">Avg time</th>
            <th scope="col" title="Succeeded runs on labelled clips">
              Labelled
            </th>
            <th scope="col" title="Your legend correct">
              You
            </th>
            <th scope="col" title="Every teammate legend correct">
              Squad
            </th>
            <th scope="col" title="Wrong legends reported at 80%+ confidence">
              Confident mistakes
            </th>
          </tr>
        </thead>
        <tbody>
          {usage.map((row) => (
            <tr
              key={`${row.provider}/${row.model}/${row.promptVersion}/${row.archivedAt?.toISOString() ?? "current"}`}
              className={row.archivedAt ? "rs-legend-archived" : undefined}
            >
              <td>
                {row.archivedAt
                  ? `Archived ${archivedFormatter.format(row.archivedAt)}`
                  : "Current"}
              </td>
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
              <td>{row.labelled}</td>
              <td>{formatAccuracy(row.playerCorrect, row.labelled)}</td>
              <td>{formatAccuracy(row.teammatesCorrect, row.labelled)}</td>
              <td>{row.labelled > 0 ? row.confidentMistakes : "–"}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
