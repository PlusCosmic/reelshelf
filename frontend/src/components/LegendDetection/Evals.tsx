import { useState, type FormEvent } from "react";
import { Link } from "@tanstack/react-router";
import { IconTrash } from "@tabler/icons-react";
import { Button, Input, Select } from "@/components/ui";
import { commonCopy } from "@/copy/common";
import { legendDetectionCopy } from "@/copy/legendDetection";
import { runsCopy } from "@/copy/runs";
import {
  useChangeBaselineLabels,
  useLegendCascade,
  useCreateLegendEval,
  useDeleteLegendEval,
  useLegendEvalMisses,
  useLegendModelPrices,
  useLegendNames,
  useSetLegendModelPrice,
} from "@/hooks/queries";
import type {
  LegendEval,
  LegendModelPrice,
} from "@/shared/services/legendDetection";
import { answerLabel, HudCrop, LegendPortrait } from "./Baseline";
import {
  accuracy,
  confusionPairs,
  costPerThousand,
  estimateEval,
  evalName,
  formatSeconds,
  formatUsd,
  formatWall,
  wilsonInterval,
} from "./legendEvals";
import {
  cascadeThresholds,
  simulateCascade,
  type CascadeResult,
} from "./legendCascade";
import { formatConfidence } from "./legendReview";
import { useRunOptions } from "./RunOptionsFields";

const copy = legendDetectionCopy.evals;
const { percent } = runsCopy.units;

/** Starts an eval on every clip in the baseline, with an estimate from the last eval of the same model. */
export function EvalForm({
  evals,
  baselineClips,
  onCreated,
}: {
  evals: LegendEval[];
  baselineClips: number;
  onCreated: (evalId: string) => void;
}) {
  const { options, fields, provider, model } = useRunOptions();
  const create = useCreateLegendEval();
  const estimate =
    provider && model
      ? estimateEval(evals, provider, model, baselineClips)
      : null;
  const estimateText =
    !provider || !model
      ? copy.form.pickModel
      : estimate === null
        ? copy.form.noEstimate
        : copy.form.estimate(
            estimate.costUsd === null ? null : formatUsd(estimate.costUsd),
            estimate.wallSeconds === null
              ? null
              : formatWall(estimate.wallSeconds),
          );

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    const confirmed = window.confirm(
      copy.form.confirm(options.model, baselineClips),
    );
    if (confirmed)
      create.mutate(options, { onSuccess: (evalId) => onCreated(evalId) });
  }

  return (
    <form className="rs-eval-form" onSubmit={onSubmit}>
      <div className="rs-eval-form-fields">{fields}</div>
      <span className="rs-meta">{estimateText}</span>
      <Button
        type="submit"
        variant="primary"
        disabled={create.isPending || baselineClips === 0}
      >
        {create.isPending ? copy.form.starting : copy.form.run(baselineClips)}
      </Button>
      {create.isError ? (
        <span className="rs-legend-error">{create.error.message}</span>
      ) : null}
    </form>
  );
}

/** One row per eval: accuracy with its 95% range, failures, cost and speed. */
export function EvalTable({
  evals,
  selectedId,
  onSelect,
}: {
  evals: LegendEval[];
  selectedId: string | undefined;
  onSelect: (evalId: string) => void;
}) {
  const remove = useDeleteLegendEval();
  // Every row's bar shares one scale, from just below the lowest range to 100%, so the ranges can be compared.
  const lowestRange = Math.min(
    ...evals
      .filter((evaluation) => evaluation.scored > 0)
      .map(
        (evaluation) =>
          wilsonInterval(evaluation.correct, evaluation.scored)[0],
      ),
    0.9,
  );
  const scaleFrom = Math.floor(lowestRange * 20) / 20;
  const onScale = (share: number) =>
    `${((share - scaleFrom) / (1 - scaleFrom)) * 100}%`;
  return (
    <div className="rs-legend-usage">
      <table className="rs-legend-table rs-eval-table">
        <thead>
          <tr>
            <th scope="col">{runsCopy.columns.model}</th>
            <th
              scope="col"
              title={copy.table.scaleTitle(Math.round(scaleFrom * 100))}
            >
              {copy.table.accuracy}
            </th>
            <th scope="col">{copy.table.answered}</th>
            <th scope="col" title={copy.table.confidentWrongTitle}>
              {copy.table.confidentWrong}
            </th>
            <th scope="col">{copy.table.cost}</th>
            <th scope="col">{runsCopy.columns.perThousandClips}</th>
            <th scope="col" title={copy.table.latencyTitle}>
              {copy.table.latency}
            </th>
            <th scope="col">{copy.table.wallTime}</th>
            <th scope="col">
              <span className="rs-visually-hidden">{commonCopy.delete}</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {evals.map((evaluation) => {
            const share = accuracy(evaluation);
            const [low, high] = wilsonInterval(
              evaluation.correct,
              evaluation.scored,
            );
            const perThousand = costPerThousand(evaluation);
            const selected = evaluation.id === selectedId;
            return (
              <tr
                key={evaluation.id}
                className={`rs-eval-row${selected ? " selected" : ""}`}
                aria-selected={selected}
                tabIndex={0}
                onClick={() => onSelect(evaluation.id)}
                onKeyDown={(event) => {
                  if (event.key === "Enter" || event.key === " ") {
                    event.preventDefault();
                    onSelect(evaluation.id);
                  }
                }}
              >
                <th scope="row">
                  <div className="rs-eval-model">{evalName(evaluation)}</div>
                  <div className="rs-meta">
                    {evaluation.provider}
                    {evaluation.queued > 0
                      ? copy.table.progress(
                          evaluation.runs - evaluation.queued,
                          evaluation.runs,
                        )
                      : ""}
                  </div>
                </th>
                <td>
                  {share === null ? (
                    "—"
                  ) : (
                    <span className="rs-eval-accuracy">
                      <span
                        className="rs-eval-accuracy-track"
                        aria-hidden="true"
                      >
                        <span
                          className="rs-eval-accuracy-range"
                          style={{
                            left: onScale(low),
                            width: `calc(${onScale(high)} - ${onScale(low)})`,
                          }}
                        />
                        <span
                          className="rs-eval-accuracy-mark"
                          style={{ left: onScale(share) }}
                        />
                      </span>
                      <strong>{percent((share * 100).toFixed(1))}</strong>
                      <span className="rs-meta">
                        ±{(((high - low) / 2) * 100).toFixed(1)}
                      </span>
                    </span>
                  )}
                </td>
                <td>
                  {evaluation.succeeded} / {evaluation.runs}
                  {evaluation.failed > 0 ? (
                    <span className="rs-legend-error">
                      {copy.table.failed(evaluation.failed)}
                    </span>
                  ) : null}
                </td>
                <td>{evaluation.confidentMistakes}</td>
                <td>
                  {evaluation.costUsd === null ? (
                    evaluation.succeeded > 0 ? (
                      <a href="#prices">{copy.table.setPrice}</a>
                    ) : (
                      "—"
                    )
                  ) : (
                    <>
                      {formatUsd(evaluation.costUsd)}
                      {evaluation.costBilled ? (
                        <span className="rs-meta"> {copy.table.billed}</span>
                      ) : null}
                    </>
                  )}
                </td>
                <td>{perThousand === null ? "—" : formatUsd(perThousand)}</td>
                <td>
                  {formatSeconds(evaluation.p50DurationMs)} /{" "}
                  {formatSeconds(evaluation.p95DurationMs)}
                </td>
                <td>
                  {evaluation.queued > 0
                    ? copy.table.running
                    : formatWall(evaluation.wallSeconds)}
                </td>
                <td>
                  <Button
                    size="sm"
                    variant="ghost"
                    aria-label={copy.table.deleteLabel(evalName(evaluation))}
                    title={copy.table.deleteTitle}
                    disabled={remove.isPending}
                    onClick={(event) => {
                      event.stopPropagation();
                      if (
                        window.confirm(
                          copy.table.confirmDelete(evalName(evaluation)),
                        )
                      )
                        remove.mutate(evaluation.id);
                    }}
                  >
                    <IconTrash size={16} />
                  </Button>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

const minCost = 0.1;
const maxCost = 100;

/**
 * Accuracy against cost per 1,000 clips (log scale), one dot per costed eval, directly labelled. Up and to the
 * left is better. The table above is the full view of the same numbers.
 */
export function AccuracyCostChart({
  evals,
  selectedId,
  onSelect,
}: {
  evals: LegendEval[];
  selectedId: string | undefined;
  onSelect: (evalId: string) => void;
}) {
  const points = evals.flatMap((evaluation) => {
    const share = accuracy(evaluation);
    const perThousand = costPerThousand(evaluation);
    return share === null || perThousand === null || perThousand <= 0
      ? []
      : [{ evaluation, share, perThousand }];
  });
  if (points.length === 0) return <p className="rs-meta">{copy.chart.empty}</p>;

  const lowest = Math.min(...points.map((point) => point.share));
  const floor = Math.max(0, Math.floor((lowest - 0.01) * 20) / 20);
  const step = 1 - floor > 0.2 ? 0.1 : 1 - floor > 0.1 ? 0.05 : 0.02;
  const x = (cost: number) =>
    ((Math.log10(Math.min(Math.max(cost, minCost), maxCost)) -
      Math.log10(minCost)) /
      (Math.log10(maxCost) - Math.log10(minCost))) *
    100;
  const y = (share: number) => ((share - floor) / (1 - floor)) * 100;
  const yTicks = Array.from(
    { length: Math.round((1 - floor) / step) + 1 },
    (_, index) => floor + index * step,
  );

  return (
    <figure className="rs-eval-chart">
      <div
        className="rs-eval-chart-plot"
        role="img"
        aria-label={copy.chart.label}
      >
        {yTicks.map((tick) => (
          <span key={tick}>
            <span
              className="rs-eval-chart-grid"
              style={{ bottom: `${y(tick)}%` }}
              aria-hidden="true"
            />
            <span
              className="rs-eval-chart-ytick"
              style={{ bottom: `${y(tick)}%` }}
            >
              {percent(Math.round(tick * 100))}
            </span>
          </span>
        ))}
        {[0.1, 1, 10, 100].map((tick) => (
          <span
            key={tick}
            className="rs-eval-chart-xtick"
            style={{ left: `${x(tick)}%` }}
          >
            {runsCopy.units.usd(tick)}
          </span>
        ))}
        {points.map(({ evaluation, share, perThousand }) => (
          <button
            key={evaluation.id}
            type="button"
            className={`rs-eval-chart-point${evaluation.id === selectedId ? " selected" : ""}`}
            style={{ left: `${x(perThousand)}%`, bottom: `${y(share)}%` }}
            title={copy.chart.point(
              evalName(evaluation),
              (share * 100).toFixed(1),
              formatUsd(perThousand),
              formatSeconds(evaluation.p50DurationMs),
            )}
            onClick={() => onSelect(evaluation.id)}
          >
            <span className="rs-eval-chart-dot" aria-hidden="true" />
            <span className="rs-eval-chart-label">{evalName(evaluation)}</span>
          </button>
        ))}
      </div>
      <figcaption className="rs-meta">{copy.chart.caption}</figcaption>
    </figure>
  );
}

/** Prices per million tokens, saved when a field is left. Edits re-cost every eval. */
export function PriceTable() {
  const prices = useLegendModelPrices();
  if (!prices.data || prices.data.length === 0) return null;
  return (
    <section id="prices" className="rs-eval-panel">
      <h2 className="rs-eval-heading">{copy.prices.heading}</h2>
      <p className="rs-meta">{copy.prices.intro}</p>
      <div className="rs-legend-usage">
        <table className="rs-legend-table">
          <thead>
            <tr>
              <th scope="col">{runsCopy.columns.model}</th>
              <th scope="col">{copy.prices.input}</th>
              <th scope="col">{runsCopy.columns.cached}</th>
              <th scope="col">{copy.prices.cacheWrite}</th>
              <th scope="col">{copy.prices.output}</th>
            </tr>
          </thead>
          <tbody>
            {prices.data.map((price) => (
              <PriceRow
                key={`${price.provider}/${price.model}`}
                price={price}
              />
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}

const priceFields = [
  ["inputPerMtok", copy.prices.fields.input],
  ["cachedInputPerMtok", copy.prices.fields.cachedInput],
  ["cacheWritePerMtok", copy.prices.fields.cacheWrite],
  ["outputPerMtok", copy.prices.fields.output],
] as const;

function PriceRow({ price }: { price: LegendModelPrice }) {
  const save = useSetLegendModelPrice();
  const [draft, setDraft] = useState(() =>
    Object.fromEntries(
      priceFields.map(([field]) => [field, price[field]?.toString() ?? ""]),
    ),
  );

  function commit() {
    const parsed = Object.fromEntries(
      priceFields.map(([field]) => {
        const value = Number.parseFloat(draft[field]);
        return [field, Number.isFinite(value) && value >= 0 ? value : null];
      }),
    ) as Pick<LegendModelPrice, (typeof priceFields)[number][0]>;
    if (priceFields.every(([field]) => parsed[field] === price[field])) return;
    save.mutate({ ...price, ...parsed });
  }

  return (
    <tr>
      <th scope="row">
        {price.model}
        <div className="rs-meta">{price.provider}</div>
      </th>
      {priceFields.map(([field, label]) => (
        <td key={field}>
          <Input
            compact
            className="rs-eval-price"
            inputMode="decimal"
            aria-label={copy.prices.fieldLabel(price.model, label)}
            value={draft[field]}
            placeholder="—"
            onChange={(event) =>
              setDraft((current) => ({
                ...current,
                [field]: event.target.value,
              }))
            }
            onBlur={commit}
            onKeyDown={(event) => {
              if (event.key === "Enter") commit();
            }}
          />
        </td>
      ))}
    </tr>
  );
}

/**
 * What an eval got wrong, beside the baseline's answer. When the model was right and the baseline wrong, one click
 * corrects the label, which re-scores every eval.
 */
export function EvalMisses({ evaluation }: { evaluation: LegendEval }) {
  const misses = useLegendEvalMisses(evaluation.id);
  const names = useLegendNames();
  const legends = names.data ?? [];
  const change = useChangeBaselineLabels();
  const pairs = confusionPairs(misses.data ?? []);

  return (
    <section className="rs-eval-panel">
      <h2 className="rs-eval-heading">
        {copy.misses.heading(evalName(evaluation))}
        <span className="rs-meta">
          {" "}
          {copy.misses.count(
            evaluation.scored - evaluation.correct,
            evaluation.scored,
          )}
        </span>
      </h2>
      {misses.isLoading ? (
        <div className="rs-empty">{commonCopy.loading}</div>
      ) : (misses.data ?? []).length === 0 ? (
        <div className="rs-empty">{copy.misses.empty}</div>
      ) : (
        <div className="rs-eval-misses-layout">
          <div className="rs-eval-misses">
            {misses.data!.map((miss) => (
              <article key={miss.runId} className="rs-eval-miss">
                {miss.frameUrls[0] ? <HudCrop src={miss.frameUrls[0]} /> : null}
                <div className="rs-eval-miss-answers">
                  {miss.status === "failed" ? (
                    <div className="rs-legend-error" title={miss.error ?? ""}>
                      {copy.misses.failed(miss.error)}
                    </div>
                  ) : (
                    <div className="rs-eval-miss-said">
                      <LegendPortrait
                        legend={miss.playerLegend}
                        legends={legends}
                        size={28}
                      />
                      <span>
                        {copy.misses.said(
                          <strong>{answerLabel(miss.playerLegend)}</strong>,
                        )}
                        {miss.confidence !== null && miss.playerLegend ? (
                          <span className="rs-meta">
                            {" "}
                            {formatConfidence(miss.confidence)}
                          </span>
                        ) : null}
                      </span>
                    </div>
                  )}
                  <div className="rs-eval-miss-said">
                    <LegendPortrait
                      legend={miss.labelLegend}
                      legends={legends}
                      size={28}
                    />
                    <span>
                      {copy.misses.baseline(
                        <strong>{answerLabel(miss.labelLegend)}</strong>,
                      )}
                    </span>
                  </div>
                </div>
                <div className="rs-eval-miss-actions">
                  <Link
                    to="/legend-detection"
                    search={{ clip: miss.clipId }}
                    className="rs-meta"
                  >
                    {miss.title}
                  </Link>
                  {miss.status === "succeeded" ? (
                    <Button
                      size="sm"
                      disabled={change.isPending}
                      onClick={() =>
                        change.mutate({
                          set: [
                            {
                              clipId: miss.clipId,
                              playerLegend: miss.playerLegend,
                            },
                          ],
                          clear: [],
                        })
                      }
                    >
                      {copy.misses.modelIsRight}
                    </Button>
                  ) : null}
                </div>
              </article>
            ))}
          </div>
          {pairs.length > 0 ? (
            <aside className="rs-eval-pairs">
              <h3 className="rs-eyebrow">{copy.misses.mixUps}</h3>
              <ol>
                {pairs.map((pair) => (
                  <li key={`${pair.label}→${pair.said}`}>
                    <span>
                      {answerLabel(pair.label)} →{" "}
                      <strong>{answerLabel(pair.said)}</strong>
                    </span>
                    <span className="rs-meta">×{pair.count}</span>
                  </li>
                ))}
              </ol>
            </aside>
          ) : null}
        </div>
      )}
    </section>
  );
}

/** The cheapest costed eval, as the model a cascade tries first. */
function defaultFirst(evals: LegendEval[]) {
  const costed = evals.filter(
    (evaluation) => costPerThousand(evaluation) !== null,
  );
  return [...costed].sort(
    (a, b) => (costPerThousand(a) ?? 0) - (costPerThousand(b) ?? 0),
  )[0];
}

/** The most accurate eval, as the model a cascade escalates to. */
function defaultEscalation(evals: LegendEval[]) {
  return [...evals].sort((a, b) => (accuracy(b) ?? 0) - (accuracy(a) ?? 0))[0];
}

/**
 * Production's cascade, worked out from two evals already run on the same clips: the first model answers every
 * clip, and the answers it is unsure of go to the second, as `LegendDetection:EscalationModel` does. Nothing is run
 * again, so any pair and threshold can be tried for free.
 */
export function CascadePanel({ evals }: { evals: LegendEval[] }) {
  const finished = evals.filter(
    (evaluation) => evaluation.queued === 0 && evaluation.scored > 0,
  );
  const [firstId, setFirstId] = useState<string>();
  const [escalationId, setEscalationId] = useState<string>();
  const first =
    finished.find((evaluation) => evaluation.id === firstId) ??
    defaultFirst(finished);
  const escalation =
    finished.find((evaluation) => evaluation.id === escalationId) ??
    defaultEscalation(finished.filter((evaluation) => evaluation !== first));
  const cascade = useLegendCascade(first?.id, escalation?.id);
  const [threshold, setThreshold] = useState<number>();

  if (finished.length < 2) return null;

  const options = finished.map((evaluation) => ({
    value: evaluation.id,
    label: copy.cascade.option(evalName(evaluation), evaluation.provider),
  }));
  const clips = cascade.data?.clips ?? [];
  const current = cascade.data?.currentEscalateBelow ?? 0.9;
  const picked = threshold ?? current;
  const rows = [...new Set([...cascadeThresholds, picked])]
    .sort((a, b) => a - b)
    .map((value) => simulateCascade(clips, value));
  const result = simulateCascade(clips, picked);

  return (
    <section className="rs-eval-panel">
      <h2 className="rs-eval-heading">{copy.cascade.heading}</h2>
      <p className="rs-meta">{copy.cascade.intro}</p>
      <div className="rs-eval-cascade-controls">
        <label>
          <span className="rs-meta">{copy.cascade.first}</span>
          <Select
            aria-label={copy.cascade.firstLabel}
            options={options}
            value={first?.id ?? ""}
            onValueChange={setFirstId}
          />
        </label>
        <label>
          <span className="rs-meta">{copy.cascade.escalateTo}</span>
          <Select
            aria-label={copy.cascade.escalationLabel}
            options={options}
            value={escalation?.id ?? ""}
            onValueChange={setEscalationId}
          />
        </label>
        <label className="rs-eval-cascade-threshold">
          <span className="rs-meta">
            {copy.cascade.threshold(
              formatConfidence(picked),
              formatConfidence(current),
            )}
          </span>
          <input
            type="range"
            min={0.5}
            max={1}
            step={0.01}
            value={picked}
            onChange={(event) => setThreshold(Number(event.target.value))}
          />
        </label>
      </div>
      {first === escalation ? (
        <div className="rs-empty">{copy.cascade.pickTwo}</div>
      ) : cascade.isLoading ? (
        <div className="rs-empty">{commonCopy.loading}</div>
      ) : cascade.isError ? (
        <div className="rs-legend-error">{cascade.error.message}</div>
      ) : clips.length === 0 ? (
        <div className="rs-empty">{copy.cascade.noCommonClips}</div>
      ) : (
        <>
          <CascadeSummary
            result={result}
            first={first!}
            escalation={escalation!}
          />
          <div className="rs-legend-usage">
            <table className="rs-legend-table">
              <thead>
                <tr>
                  <th scope="col">{copy.cascade.escalateBelow}</th>
                  <th scope="col">{copy.cascade.escalated}</th>
                  <th scope="col">{copy.cascade.accuracy}</th>
                  <th scope="col">{copy.cascade.wrong}</th>
                  <th scope="col">{runsCopy.columns.perThousandClips}</th>
                  <th scope="col">{copy.cascade.meanTime}</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => (
                  <CascadeRow
                    key={row.threshold}
                    row={row}
                    selected={row.threshold === picked}
                    onSelect={() => setThreshold(row.threshold)}
                  />
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </section>
  );
}

function CascadeSummary({
  result,
  first,
  escalation,
}: {
  result: CascadeResult;
  first: LegendEval;
  escalation: LegendEval;
}) {
  const share = result.correct / result.clips;
  const [low, high] = wilsonInterval(result.correct, result.clips);
  const perThousand =
    result.costUsd === null ? null : (result.costUsd / result.clips) * 1000;
  const alone = (evaluation: LegendEval) => {
    const value = accuracy(evaluation);
    const cost = costPerThousand(evaluation);
    return copy.cascade.alone(
      value === null ? "—" : percent((value * 100).toFixed(1)),
      cost === null ? null : formatUsd(cost),
    );
  };
  return (
    <div className="rs-eval-cascade-summary">
      <div>
        <div className="rs-eyebrow">{copy.cascade.accuracy}</div>
        <strong>{percent((share * 100).toFixed(1))}</strong>
        <span className="rs-meta">
          {" "}
          ±{(((high - low) / 2) * 100).toFixed(1)}
        </span>
      </div>
      <div>
        <div className="rs-eyebrow">{copy.cascade.escalated}</div>
        <strong>
          {percent(((result.escalated / result.clips) * 100).toFixed(1))}
        </strong>
        <span className="rs-meta">
          {" "}
          {copy.cascade.escalatedOf(result.escalated, result.clips)}
        </span>
      </div>
      <div>
        <div className="rs-eyebrow">{runsCopy.columns.perThousandClips}</div>
        <strong>
          {perThousand === null
            ? copy.cascade.setPrices
            : formatUsd(perThousand)}
        </strong>
      </div>
      <div>
        <div className="rs-eyebrow">{copy.cascade.meanTime}</div>
        <strong>{formatSeconds(result.meanLatencyMs)}</strong>
      </div>
      <p className="rs-meta">
        {copy.cascade.aloneSummary(
          evalName(first),
          alone(first),
          evalName(escalation),
          alone(escalation),
        )}
      </p>
    </div>
  );
}

function CascadeRow({
  row,
  selected,
  onSelect,
}: {
  row: CascadeResult;
  selected: boolean;
  onSelect: () => void;
}) {
  return (
    <tr
      className={`rs-eval-row${selected ? " selected" : ""}`}
      aria-selected={selected}
      tabIndex={0}
      onClick={onSelect}
      onKeyDown={(event) => {
        if (event.key === "Enter" || event.key === " ") {
          event.preventDefault();
          onSelect();
        }
      }}
    >
      <th scope="row">{formatConfidence(row.threshold)}</th>
      <td>
        {percent(((row.escalated / row.clips) * 100).toFixed(1))}
        <span className="rs-meta"> ({row.escalated})</span>
      </td>
      <td>{percent(((row.correct / row.clips) * 100).toFixed(1))}</td>
      <td>{row.clips - row.correct}</td>
      <td>
        {row.costUsd === null
          ? "—"
          : formatUsd((row.costUsd / row.clips) * 1000)}
      </td>
      <td>{formatSeconds(row.meanLatencyMs)}</td>
    </tr>
  );
}
