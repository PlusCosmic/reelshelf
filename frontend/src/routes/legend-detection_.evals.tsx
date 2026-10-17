import { createFileRoute, Link, useNavigate } from "@tanstack/react-router";
import { useMemo } from "react";
import {
  AccuracyCostChart,
  CascadePanel,
  EvalForm,
  EvalMisses,
  EvalTable,
  PriceTable,
} from "@/components/LegendDetection/Evals";
import {
  useCurrentUser,
  useLegendBaseline,
  useLegendEvals,
} from "@/hooks/queries";

type LegendEvalsSearch = { eval?: string };

export const Route = createFileRoute("/legend-detection_/evals")({
  component: LegendEvalsRoute,
  validateSearch: (search: Record<string, unknown>): LegendEvalsSearch => ({
    eval: typeof search.eval === "string" ? search.eval : undefined,
  }),
});

function LegendEvalsRoute() {
  const { data: user, isLoading } = useCurrentUser();

  if (isLoading) return <div className="rs-section rs-empty">Loading…</div>;
  if (!user?.isAdmin)
    return (
      <div className="rs-section rs-empty">
        Legend evals are only available to admins.
      </div>
    );
  return <LegendEvals />;
}

function LegendEvals() {
  const search = Route.useSearch();
  const navigate = useNavigate({ from: Route.fullPath });
  const evalsQuery = useLegendEvals();
  const evals = useMemo(() => evalsQuery.data ?? [], [evalsQuery.data]);
  const baseline = useLegendBaseline();
  const baselineClips = (baseline.data ?? []).filter(
    (clip) => clip.label !== null,
  ).length;
  const selected =
    evals.find((evaluation) => evaluation.id === search.eval) ?? evals[0];

  const select = (evalId: string) =>
    void navigate({ search: { eval: evalId }, replace: true });

  return (
    <div className="rs-legend-page">
      <header className="rs-legend-header">
        <div>
          <div className="rs-eyebrow">Legend detection · Evals</div>
          <h1 className="rs-display rs-h2">Which model is worth it?</h1>
          <p className="rs-meta rs-baseline-intro">
            Each eval runs one model on every confirmed clip and is scored
            against the labels as they are now, so fixing a label re-scores
            every eval. Eval runs never change a clip's legend.
          </p>
        </div>
        <div className="rs-legend-backfill">
          <Link to="/legend-detection/baseline" className="rs-meta">
            Baseline: {baselineClips} clips confirmed
          </Link>
          <Link to="/legend-detection" className="rs-meta">
            Back to review
          </Link>
        </div>
      </header>

      <EvalForm
        evals={evals}
        baselineClips={baselineClips}
        onCreated={select}
      />

      {evalsQuery.isLoading ? (
        <div className="rs-empty">Loading evals…</div>
      ) : evalsQuery.isError ? (
        <div className="rs-empty">Evals could not be loaded.</div>
      ) : evals.length === 0 ? (
        <div className="rs-empty">
          No evals yet. Confirm clips on the baseline, then run a model on them.
        </div>
      ) : (
        <>
          <EvalTable
            evals={evals}
            selectedId={selected?.id}
            onSelect={select}
          />
          <div className="rs-eval-columns">
            <section className="rs-eval-panel">
              <h2 className="rs-eval-heading">Accuracy against cost</h2>
              <AccuracyCostChart
                evals={evals}
                selectedId={selected?.id}
                onSelect={select}
              />
            </section>
            <PriceTable />
          </div>
          <CascadePanel evals={evals} />
          {selected ? (
            <EvalMisses key={selected.id} evaluation={selected} />
          ) : null}
        </>
      )}
    </div>
  );
}
