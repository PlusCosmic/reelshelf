import { useState, type FormEvent } from "react";
import { Link } from "@tanstack/react-router";
import { IconTrash } from "@tabler/icons-react";
import { Button, Input } from "@/components/ui";
import {
  useChangeBaselineLabels,
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
import { formatConfidence } from "./legendReview";
import { useRunOptions } from "./RunOptionsFields";

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
      ? "Pick a provider and model to see an estimate."
      : estimate === null
        ? "No finished eval of this model to estimate from."
        : `About ${estimate.costUsd === null ? "an unknown cost" : formatUsd(estimate.costUsd)}${estimate.wallSeconds === null ? "" : ` and ${formatWall(estimate.wallSeconds)}`}, from its last eval.`;

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    const confirmed = window.confirm(
      `Run ${options.model ?? "the default model"} on all ${baselineClips} baseline clips? Each run is a paid model call.`,
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
        {create.isPending
          ? "Starting…"
          : `Run on baseline (${baselineClips} clips)`}
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
            <th scope="col">Model</th>
            <th
              scope="col"
              title={`Bars run from ${Math.round(scaleFrom * 100)}% to 100%`}
            >
              Accuracy (95% range)
            </th>
            <th scope="col">Answered</th>
            <th scope="col" title="Wrong legends given at 80%+ confidence">
              Confident wrong
            </th>
            <th scope="col">Cost</th>
            <th scope="col">Per 1k clips</th>
            <th scope="col" title="The model call alone, for succeeded runs">
              Latency p50 / p95
            </th>
            <th scope="col">Wall time</th>
            <th scope="col">
              <span className="rs-visually-hidden">Delete</span>
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
                      ? ` · running ${evaluation.runs - evaluation.queued}/${evaluation.runs}`
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
                      <strong>{(share * 100).toFixed(1)}%</strong>
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
                      {" "}
                      · {evaluation.failed} failed
                    </span>
                  ) : null}
                </td>
                <td>{evaluation.confidentMistakes}</td>
                <td>
                  {evaluation.costUsd === null ? (
                    evaluation.succeeded > 0 ? (
                      <a href="#prices">Set price</a>
                    ) : (
                      "—"
                    )
                  ) : (
                    <>
                      {formatUsd(evaluation.costUsd)}
                      {evaluation.costBilled ? (
                        <span className="rs-meta"> billed</span>
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
                    ? "running"
                    : formatWall(evaluation.wallSeconds)}
                </td>
                <td>
                  <Button
                    size="sm"
                    variant="ghost"
                    aria-label={`Delete ${evalName(evaluation)} eval`}
                    title="Delete eval"
                    disabled={remove.isPending}
                    onClick={(event) => {
                      event.stopPropagation();
                      if (
                        window.confirm(
                          `Delete the ${evalName(evaluation)} eval and its runs? Queued runs are cancelled.`,
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
  if (points.length === 0)
    return (
      <p className="rs-meta">
        Costed evals appear here once they have results and a price.
      </p>
    );

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
        aria-label="Accuracy against cost per 1,000 clips for each eval"
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
              {Math.round(tick * 100)}%
            </span>
          </span>
        ))}
        {[0.1, 1, 10, 100].map((tick) => (
          <span
            key={tick}
            className="rs-eval-chart-xtick"
            style={{ left: `${x(tick)}%` }}
          >
            ${tick}
          </span>
        ))}
        {points.map(({ evaluation, share, perThousand }) => (
          <button
            key={evaluation.id}
            type="button"
            className={`rs-eval-chart-point${evaluation.id === selectedId ? " selected" : ""}`}
            style={{ left: `${x(perThousand)}%`, bottom: `${y(share)}%` }}
            title={`${evalName(evaluation)}: ${(share * 100).toFixed(1)}% at ${formatUsd(perThousand)} per 1k clips, p50 ${formatSeconds(evaluation.p50DurationMs)}`}
            onClick={() => onSelect(evaluation.id)}
          >
            <span className="rs-eval-chart-dot" aria-hidden="true" />
            <span className="rs-eval-chart-label">{evalName(evaluation)}</span>
          </button>
        ))}
      </div>
      <figcaption className="rs-meta">
        Accuracy against cost per 1,000 clips, log scale. Up and to the left is
        better.
      </figcaption>
    </figure>
  );
}

/** Prices per million tokens, saved when a field is left. Edits re-cost every eval. */
export function PriceTable() {
  const prices = useLegendModelPrices();
  if (!prices.data || prices.data.length === 0) return null;
  return (
    <section id="prices" className="rs-eval-panel">
      <h2 className="rs-eval-heading">Prices, $ per million tokens</h2>
      <p className="rs-meta">
        Used for evals the provider doesn't bill per run. Cached and cache-write
        prices fall back to input when empty.
      </p>
      <div className="rs-legend-usage">
        <table className="rs-legend-table">
          <thead>
            <tr>
              <th scope="col">Model</th>
              <th scope="col">Input</th>
              <th scope="col">Cached</th>
              <th scope="col">Cache write</th>
              <th scope="col">Output</th>
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
  ["inputPerMtok", "input"],
  ["cachedInputPerMtok", "cached input"],
  ["cacheWritePerMtok", "cache write"],
  ["outputPerMtok", "output"],
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
            aria-label={`${price.model} ${label} price`}
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
        Wrong answers · {evalName(evaluation)}
        <span className="rs-meta">
          {" "}
          {evaluation.scored - evaluation.correct} of {evaluation.scored}
        </span>
      </h2>
      {misses.isLoading ? (
        <div className="rs-empty">Loading…</div>
      ) : (misses.data ?? []).length === 0 ? (
        <div className="rs-empty">No wrong answers in this eval.</div>
      ) : (
        <div className="rs-eval-misses-layout">
          <div className="rs-eval-misses">
            {misses.data!.map((miss) => (
              <article key={miss.runId} className="rs-eval-miss">
                {miss.frameUrls[0] ? <HudCrop src={miss.frameUrls[0]} /> : null}
                <div className="rs-eval-miss-answers">
                  {miss.status === "failed" ? (
                    <div className="rs-legend-error" title={miss.error ?? ""}>
                      Failed: {miss.error ?? "no error recorded"}
                    </div>
                  ) : (
                    <div className="rs-eval-miss-said">
                      <LegendPortrait
                        legend={miss.playerLegend}
                        legends={legends}
                        size={28}
                      />
                      <span>
                        Said <strong>{answerLabel(miss.playerLegend)}</strong>
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
                      Baseline <strong>{answerLabel(miss.labelLegend)}</strong>
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
                      Model is right
                    </Button>
                  ) : null}
                </div>
              </article>
            ))}
          </div>
          {pairs.length > 0 ? (
            <aside className="rs-eval-pairs">
              <h3 className="rs-eyebrow">Most common mix-ups</h3>
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
