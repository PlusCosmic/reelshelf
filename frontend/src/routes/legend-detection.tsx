import { createFileRoute, Link, useNavigate } from "@tanstack/react-router";
import { IconCheck, IconX } from "@tabler/icons-react";
import { useEffect, useMemo, useState } from "react";
import { Chip } from "@/components/Reelshelf/ReelshelfPrimitives";
import { formatDate } from "@/components/Reelshelf/reelshelf-model";
import { Badge, Button } from "@/components/ui";
import { LegendReviewDetail } from "@/components/LegendDetection/LegendReviewDetail";
import { LegendUsageTable } from "@/components/LegendDetection/LegendUsageTable";
import { ReasoningEffortSelect } from "@/components/LegendDetection/ReasoningEffortSelect";
import {
  countByFilter,
  formatConfidence,
  labelFromRun,
  labelVerdict,
  matchesFilter,
  reviewFilters,
  reviewTone,
  type ReviewFilter,
} from "@/components/LegendDetection/legendReview";
import { commonCopy } from "@/copy/common";
import { legendDetectionCopy } from "@/copy/legendDetection";
import { runsCopy } from "@/copy/runs";
import {
  useArchiveLegendRuns,
  useBackfillLegendDetection,
  useCurrentUser,
  useLegendReviewClips,
  useLegendUsage,
  useSetLegendLabel,
} from "@/hooks/queries";
import {
  isRunActive,
  type LegendDetectionReviewClip,
} from "@/shared/services/legendDetection";

const copy = legendDetectionCopy.review;

type LegendDetectionSearch = {
  clip?: string;
  filter?: ReviewFilter;
};

export const Route = createFileRoute("/legend-detection")({
  component: LegendDetectionRoute,
  validateSearch: (search: Record<string, unknown>): LegendDetectionSearch => ({
    clip: typeof search.clip === "string" ? search.clip : undefined,
    filter: reviewFilters.some(({ value }) => value === search.filter)
      ? (search.filter as ReviewFilter)
      : undefined,
  }),
});

function LegendDetectionRoute() {
  const { data: user, isLoading } = useCurrentUser();

  if (isLoading)
    return <div className="rs-section rs-empty">{commonCopy.loading}</div>;
  if (!user?.isAdmin)
    return <div className="rs-section rs-empty">{copy.adminsOnly}</div>;
  return <LegendReview />;
}

function LegendReview() {
  const search = Route.useSearch();
  const navigate = useNavigate({ from: Route.fullPath });
  const filter = search.filter ?? "all";
  const clipsQuery = useLegendReviewClips();
  const clips = useMemo(() => clipsQuery.data ?? [], [clipsQuery.data]);
  const anyActive = clips.some((clip) => isRunActive(clip.latestRun));
  const usage = useLegendUsage(true, anyActive);
  const backfill = useBackfillLegendDetection();
  const [backfillEffort, setBackfillEffort] = useState("");
  const archive = useArchiveLegendRuns();
  const setLabel = useSetLegendLabel();
  const mutateLabel = setLabel.mutate;

  const counts = useMemo(() => countByFilter(clips), [clips]);
  const visible = useMemo(
    () => clips.filter((clip) => matchesFilter(clip, filter)),
    [clips, filter],
  );
  // A clip picked under another filter stays open until another is chosen.
  const selected =
    clips.find((clip) => clip.clipId === search.clip) ?? visible[0];

  const setSearch = (next: LegendDetectionSearch) =>
    void navigate({
      search: (previous) => ({ ...previous, ...next }),
      replace: true,
    });

  // j and k step through the visible list, as in most review tools; c confirms the latest result as the
  // clip's label and moves on, so labelling a run of correct answers is one key each.
  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      if (event.key !== "j" && event.key !== "k" && event.key !== "c") return;
      if (event.metaKey || event.ctrlKey || event.altKey) return;
      if (
        event.target instanceof HTMLElement &&
        event.target.closest("input, textarea, select, [contenteditable]")
      )
        return;
      const index = visible.findIndex(
        (clip) => clip.clipId === selected?.clipId,
      );
      if (event.key === "c") {
        const run = selected?.latestRun;
        if (!selected || run?.status !== "succeeded") return;
        const detected = labelFromRun(run);
        mutateLabel({ clipId: selected.clipId, ...detected });
      }
      const next = visible[index + (event.key === "k" ? -1 : 1)];
      if (next) {
        event.preventDefault();
        void navigate({
          search: (previous) => ({ ...previous, clip: next.clipId }),
          replace: true,
        });
      }
    }
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [mutateLabel, navigate, selected, visible]);

  // Keyed on the id so a poll refreshing the clip data does not scroll the list back.
  const selectedId = selected?.clipId;
  useEffect(() => {
    if (!selectedId) return;
    document
      .querySelector(`[data-legend-clip="${selectedId}"]`)
      ?.scrollIntoView({ block: "nearest" });
  }, [selectedId]);

  function onBackfill() {
    const effort = backfillEffort
      ? runsCopy.reasoningEffort.option(backfillEffort)
      : copy.defaultReasoning;
    const confirmed = window.confirm(copy.confirmBackfill(effort));
    if (confirmed) backfill.mutate(backfillEffort || null);
  }

  function onArchive() {
    const confirmed = window.confirm(copy.confirmArchive);
    if (confirmed) archive.mutate();
  }

  return (
    <div className="rs-legend-page">
      <header className="rs-legend-header">
        <div>
          <div className="rs-eyebrow">{copy.eyebrow}</div>
          <h1 className="rs-display rs-h2">{copy.title}</h1>
        </div>
        <div className="rs-legend-backfill">
          <Link
            to="/legend-detection/baseline"
            className="rs-ui-button rs-ui-button-secondary rs-ui-button-md"
          >
            {copy.baseline}
          </Link>
          <Link
            to="/legend-detection/evals"
            className="rs-ui-button rs-ui-button-secondary rs-ui-button-md"
          >
            {copy.evals}
          </Link>
          <Button onClick={onArchive} disabled={archive.isPending}>
            {archive.isPending ? copy.archiving : copy.startFresh}
          </Button>
          <ReasoningEffortSelect
            value={backfillEffort}
            onValueChange={setBackfillEffort}
          />
          <Button
            variant="primary"
            onClick={onBackfill}
            disabled={backfill.isPending}
          >
            {backfill.isPending
              ? runsCopy.rerun.queueing
              : runsCopy.backfillAll}
          </Button>
          {backfill.isSuccess ? (
            <span className="rs-meta">
              {runsCopy.queuedClips(backfill.data)}
            </span>
          ) : null}
          {archive.isSuccess ? (
            <span className="rs-meta">{copy.archived(archive.data)}</span>
          ) : null}
          {archive.isError ? (
            <span className="rs-legend-error">{archive.error.message}</span>
          ) : null}
          {backfill.isError ? (
            <span className="rs-legend-error">{backfill.error.message}</span>
          ) : null}
        </div>
      </header>

      <LegendUsageTable usage={usage.data ?? []} />

      <div className="rs-chip-row rs-legend-filters">
        {reviewFilters.map(({ value, label }) => (
          <Chip
            key={value}
            active={filter === value}
            onClick={() => setSearch({ filter: value })}
          >
            {label} · {counts[value]}
          </Chip>
        ))}
        <span className="rs-meta rs-legend-hint">{copy.hint}</span>
      </div>

      {clipsQuery.isLoading ? (
        <div className="rs-empty">{runsCopy.loadingClips}</div>
      ) : clipsQuery.isError ? (
        <div className="rs-empty">{runsCopy.clipsNotLoaded}</div>
      ) : clips.length === 0 ? (
        <div className="rs-empty">{legendDetectionCopy.noApexClips}</div>
      ) : (
        <div className="rs-legend-layout">
          <ol className="rs-legend-list">
            {visible.length === 0 ? (
              <li className="rs-empty">{runsCopy.noClipsMatch}</li>
            ) : null}
            {visible.map((clip) => (
              <li key={clip.clipId}>
                <ClipRow
                  clip={clip}
                  selected={clip.clipId === selected?.clipId}
                  onSelect={() => setSearch({ clip: clip.clipId })}
                />
              </li>
            ))}
          </ol>
          {selected ? (
            <LegendReviewDetail key={selected.clipId} clip={selected} />
          ) : null}
        </div>
      )}
    </div>
  );
}

function ClipRow({
  clip,
  selected,
  onSelect,
}: {
  clip: LegendDetectionReviewClip;
  selected: boolean;
  onSelect: () => void;
}) {
  const run = clip.latestRun;
  const verdict = labelVerdict(run, clip.label);
  return (
    <button
      type="button"
      className={`rs-legend-row${selected ? " selected" : ""}`}
      data-legend-clip={clip.clipId}
      aria-current={selected ? "true" : undefined}
      onClick={onSelect}
    >
      <img
        className="rs-legend-row-thumb"
        src={clip.frameUrls[0]}
        alt=""
        loading="lazy"
      />
      <span className="rs-legend-row-text">
        <strong>{clip.title}</strong>
        <span className="rs-meta">
          {clip.ownerName} · {formatDate(clip.createdAt)}
        </span>
      </span>
      <span className="rs-legend-row-status">
        {run === null ? (
          <Badge>{copy.notRun}</Badge>
        ) : run.status === "succeeded" ? (
          <Badge tone={reviewTone(run)}>
            {run.playerLegend ?? copy.noLegend}{" "}
            {run.playerLegend
              ? formatConfidence(run.playerLegendConfidence)
              : null}
          </Badge>
        ) : (
          <Badge tone={run.status === "failed" ? "danger" : "neutral"}>
            {run.status}
          </Badge>
        )}
        {verdict === "correct" ? (
          <IconCheck
            size={16}
            className="rs-legend-verdict correct"
            aria-label={copy.matchesLabel}
          />
        ) : verdict === "wrong" ? (
          <IconX
            size={16}
            className="rs-legend-verdict wrong"
            aria-label={copy.doesntMatchLabel}
          />
        ) : null}
      </span>
    </button>
  );
}
