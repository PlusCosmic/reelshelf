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
import { commonCopy } from "@/copy/common";
import { legendDetectionCopy } from "@/copy/legendDetection";
import {
  useCurrentUser,
  useLegendBaseline,
  useLegendEvals,
} from "@/hooks/queries";

type LegendEvalsSearch = { eval?: string };

const copy = legendDetectionCopy.evals;

export const Route = createFileRoute("/legend-detection_/evals")({
  component: LegendEvalsRoute,
  validateSearch: (search: Record<string, unknown>): LegendEvalsSearch => ({
    eval: typeof search.eval === "string" ? search.eval : undefined,
  }),
});

function LegendEvalsRoute() {
  const { data: user, isLoading } = useCurrentUser();

  if (isLoading)
    return <div className="rs-section rs-empty">{commonCopy.loading}</div>;
  if (!user?.isAdmin)
    return <div className="rs-section rs-empty">{copy.adminsOnly}</div>;
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
          <div className="rs-eyebrow">{copy.eyebrow}</div>
          <h1 className="rs-display rs-h2">{copy.title}</h1>
          <p className="rs-meta rs-baseline-intro">{copy.intro}</p>
        </div>
        <div className="rs-legend-backfill">
          <Link to="/legend-detection/baseline" className="rs-meta">
            {copy.baselineLink(baselineClips)}
          </Link>
          <Link to="/legend-detection" className="rs-meta">
            {legendDetectionCopy.backToReview}
          </Link>
        </div>
      </header>

      <EvalForm
        evals={evals}
        baselineClips={baselineClips}
        onCreated={select}
      />

      {evalsQuery.isLoading ? (
        <div className="rs-empty">{copy.loading}</div>
      ) : evalsQuery.isError ? (
        <div className="rs-empty">{copy.notLoaded}</div>
      ) : evals.length === 0 ? (
        <div className="rs-empty">{copy.empty}</div>
      ) : (
        <>
          <EvalTable
            evals={evals}
            selectedId={selected?.id}
            onSelect={select}
          />
          <div className="rs-eval-columns">
            <section className="rs-eval-panel">
              <h2 className="rs-eval-heading">{copy.chartHeading}</h2>
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
