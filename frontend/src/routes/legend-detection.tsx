import { createFileRoute, useNavigate } from "@tanstack/react-router";
import { useEffect, useMemo } from "react";
import { Chip } from "@/components/Reelshelf/ReelshelfPrimitives";
import { formatDate } from "@/components/Reelshelf/reelshelf-model";
import { Badge, Button } from "@/components/ui";
import { LegendReviewDetail } from "@/components/LegendDetection/LegendReviewDetail";
import { LegendUsageTable } from "@/components/LegendDetection/LegendUsageTable";
import {
  countByFilter,
  formatConfidence,
  matchesFilter,
  reviewFilters,
  reviewTone,
  type ReviewFilter,
} from "@/components/LegendDetection/legendReview";
import {
  useBackfillLegendDetection,
  useCurrentUser,
  useLegendReviewClips,
  useLegendUsage,
} from "@/hooks/queries";
import {
  isRunActive,
  type LegendDetectionReviewClip,
} from "@/shared/services/legendDetection";

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

  if (isLoading) return <div className="rs-section rs-empty">Loading…</div>;
  if (!user?.isAdmin)
    return (
      <div className="rs-section rs-empty">
        Legend detection review is only available to admins.
      </div>
    );
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

  // j and k step through the visible list, as in most review tools.
  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      if (event.key !== "j" && event.key !== "k") return;
      if (event.metaKey || event.ctrlKey || event.altKey) return;
      if (
        event.target instanceof HTMLElement &&
        event.target.closest("input, textarea, select, [contenteditable]")
      )
        return;
      const index = visible.findIndex(
        (clip) => clip.clipId === selected?.clipId,
      );
      const next = visible[index + (event.key === "j" ? 1 : -1)];
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
  }, [navigate, selected?.clipId, visible]);

  // Keyed on the id so a poll refreshing the clip data does not scroll the list back.
  const selectedId = selected?.clipId;
  useEffect(() => {
    if (!selectedId) return;
    document
      .querySelector(`[data-legend-clip="${selectedId}"]`)
      ?.scrollIntoView({ block: "nearest" });
  }, [selectedId]);

  function onBackfill() {
    const confirmed = window.confirm(
      "Queue a detection run for every Apex Legends clip that has never had one? Each run is a paid model call.",
    );
    if (confirmed) backfill.mutate();
  }

  return (
    <div className="rs-legend-page">
      <header className="rs-legend-header">
        <div>
          <div className="rs-eyebrow">Legend detection</div>
          <h1 className="rs-display rs-h2">Check what the model saw.</h1>
        </div>
        <div className="rs-legend-backfill">
          <Button
            variant="primary"
            onClick={onBackfill}
            disabled={backfill.isPending}
          >
            {backfill.isPending ? "Queueing…" : "Backfill all clips"}
          </Button>
          {backfill.isSuccess ? (
            <span className="rs-meta">
              Queued {backfill.data} {backfill.data === 1 ? "clip" : "clips"}.
            </span>
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
        <span className="rs-meta rs-legend-hint">j / k to move</span>
      </div>

      {clipsQuery.isLoading ? (
        <div className="rs-empty">Loading clips…</div>
      ) : clipsQuery.isError ? (
        <div className="rs-empty">Clips could not be loaded.</div>
      ) : clips.length === 0 ? (
        <div className="rs-empty">You have no Apex Legends clips.</div>
      ) : (
        <div className="rs-legend-layout">
          <ol className="rs-legend-list">
            {visible.length === 0 ? (
              <li className="rs-empty">No clips match this filter.</li>
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
        <span className="rs-meta">{formatDate(clip.createdAt)}</span>
      </span>
      <span className="rs-legend-row-status">
        {run === null ? (
          <Badge>Not run</Badge>
        ) : run.status === "succeeded" ? (
          <Badge tone={reviewTone(run)}>
            {run.playerLegend ?? "No legend"}{" "}
            {run.playerLegend
              ? formatConfidence(run.playerLegendConfidence)
              : null}
          </Badge>
        ) : (
          <Badge tone={run.status === "failed" ? "danger" : "neutral"}>
            {run.status}
          </Badge>
        )}
      </span>
    </button>
  );
}
