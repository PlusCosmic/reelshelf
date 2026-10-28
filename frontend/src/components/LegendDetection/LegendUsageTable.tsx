import { legendDetectionCopy } from "@/copy/legendDetection";
import { runsCopy } from "@/copy/runs";
import type { LegendDetectionUsage } from "@/shared/services/legendDetection";
import {
  cachedShare,
  formatAccuracy,
  formatReasoningEffort,
  formatTokens,
} from "./legendReview";

const copy = legendDetectionCopy.usage;

const archivedFormatter = new Intl.DateTimeFormat(undefined, {
  month: "short",
  day: "numeric",
  hour: "numeric",
  minute: "2-digit",
});

/**
 * Runs and tokens per model, reasoning effort and prompt version, covering every user's clips. Each "Start fresh" leaves its
 * runs as an archived batch, so backfills can be compared.
 */
export function LegendUsageTable({ usage }: { usage: LegendDetectionUsage[] }) {
  if (usage.length === 0) return null;

  return (
    <div className="rs-legend-usage">
      <table className="rs-legend-table">
        <thead>
          <tr>
            <th scope="col">{copy.batch}</th>
            <th scope="col">{runsCopy.columns.model}</th>
            <th scope="col">{runsCopy.columns.reasoning}</th>
            <th scope="col">{runsCopy.columns.prompt}</th>
            <th scope="col">{runsCopy.columns.succeeded}</th>
            <th scope="col">{runsCopy.columns.failed}</th>
            <th scope="col">{runsCopy.columns.queued}</th>
            <th scope="col">{runsCopy.columns.inputTokens}</th>
            <th scope="col">{runsCopy.columns.cached}</th>
            <th scope="col">{runsCopy.columns.outputTokens}</th>
            <th scope="col">{runsCopy.columns.avgTime}</th>
            <th scope="col" title={copy.labelledTitle}>
              {copy.labelled}
            </th>
            <th scope="col" title={copy.youTitle}>
              {copy.you}
            </th>
            <th scope="col" title={copy.squadTitle}>
              {copy.squad}
            </th>
            <th scope="col" title={copy.confidentMistakesTitle}>
              {copy.confidentMistakes}
            </th>
          </tr>
        </thead>
        <tbody>
          {usage.map((row) => (
            <tr
              key={`${row.provider}/${row.model}/${row.reasoningEffort}/${row.promptVersion}/${row.archivedAt?.toISOString() ?? "current"}`}
              className={row.archivedAt ? "rs-legend-archived" : undefined}
            >
              <td>
                {row.archivedAt
                  ? copy.archived(archivedFormatter.format(row.archivedAt))
                  : copy.current}
              </td>
              <td>{row.model}</td>
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
              <td>
                {row.averageDurationMs === null
                  ? "–"
                  : runsCopy.units.durationMs(row.averageDurationMs)}
              </td>
              <td>{row.labelled}</td>
              <td>{formatAccuracy(row.playerCorrect, row.labelled)}</td>
              <td>{formatAccuracy(row.teammatesCorrect, row.squadLabelled)}</td>
              <td>{row.labelled > 0 ? row.confidentMistakes : "–"}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
