import { createFileRoute, useNavigate } from "@tanstack/react-router";
import { useEffect, useMemo } from "react";
import { Chip } from "@/components/Reelshelf/ReelshelfPrimitives";
import { formatDate } from "@/components/Reelshelf/reelshelf-model";
import { Badge, Button } from "@/components/ui";
import { TranscriptionReviewDetail } from "@/components/ClipTranscription/TranscriptionReviewDetail";
import { SummaryUsageTable } from "@/components/ClipTranscription/SummaryUsageTable";
import { TranscriptionUsageTable } from "@/components/ClipTranscription/TranscriptionUsageTable";
import {
  countByTranscriptionFilter,
  matchesTranscriptionFilter,
  transcriptOutcome,
  transcriptionFilters,
  type TranscriptionFilter,
} from "@/components/ClipTranscription/transcriptionReview";
import { clipTranscriptionCopy } from "@/copy/clipTranscription";
import { commonCopy } from "@/copy/common";
import { runsCopy } from "@/copy/runs";
import {
  useBackfillClipSummary,
  useBackfillClipTranscription,
  useCurrentUser,
  useRetryEmptyTranscriptions,
  useSummaryUsage,
  useTranscriptionReviewClips,
  useTranscriptionUsage,
} from "@/hooks/queries";
import {
  isTranscriptionActive,
  type ClipTranscriptionReviewClip,
} from "@/shared/services/clipTranscription";

const copy = clipTranscriptionCopy;

type ClipTranscriptionSearch = {
  clip?: string;
  filter?: TranscriptionFilter;
};

export const Route = createFileRoute("/clip-transcription")({
  component: ClipTranscriptionRoute,
  validateSearch: (
    search: Record<string, unknown>,
  ): ClipTranscriptionSearch => ({
    clip: typeof search.clip === "string" ? search.clip : undefined,
    filter: transcriptionFilters.some(({ value }) => value === search.filter)
      ? (search.filter as TranscriptionFilter)
      : undefined,
  }),
});

function ClipTranscriptionRoute() {
  const { data: user, isLoading } = useCurrentUser();

  if (isLoading)
    return <div className="rs-section rs-empty">{commonCopy.loading}</div>;
  if (!user?.isAdmin)
    return <div className="rs-section rs-empty">{copy.adminsOnly}</div>;
  return <TranscriptionReview />;
}

function TranscriptionReview() {
  const search = Route.useSearch();
  const navigate = useNavigate({ from: Route.fullPath });
  const filter = search.filter ?? "all";
  const clipsQuery = useTranscriptionReviewClips();
  const clips = useMemo(() => clipsQuery.data ?? [], [clipsQuery.data]);
  const anyActive = clips.some((clip) => isTranscriptionActive(clip.latestRun));
  const usage = useTranscriptionUsage(true, anyActive);
  const backfill = useBackfillClipTranscription();
  const retryEmpty = useRetryEmptyTranscriptions();
  const summaryUsage = useSummaryUsage(true, anyActive);
  const backfillSummaries = useBackfillClipSummary();

  const counts = useMemo(() => countByTranscriptionFilter(clips), [clips]);
  const visible = useMemo(
    () => clips.filter((clip) => matchesTranscriptionFilter(clip, filter)),
    [clips, filter],
  );
  // A clip picked under another filter stays open until another is chosen.
  const selected =
    clips.find((clip) => clip.clipId === search.clip) ?? visible[0];

  const setSearch = (next: ClipTranscriptionSearch) =>
    void navigate({
      search: (previous) => ({ ...previous, ...next }),
      replace: true,
    });

  // j and k step through the visible list, as on the legend detection page.
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
  }, [navigate, selected, visible]);

  // Keyed on the id so a poll refreshing the clip data does not scroll the list back.
  const selectedId = selected?.clipId;
  useEffect(() => {
    if (!selectedId) return;
    document
      .querySelector(`[data-transcription-clip="${selectedId}"]`)
      ?.scrollIntoView({ block: "nearest" });
  }, [selectedId]);

  function onBackfill() {
    const confirmed = window.confirm(copy.confirmBackfill);
    if (confirmed) backfill.mutate();
  }

  function onRetryEmpty() {
    const confirmed = window.confirm(copy.confirmRetryEmpty);
    if (confirmed) retryEmpty.mutate();
  }

  function onBackfillSummaries() {
    const confirmed = window.confirm(copy.confirmBackfillSummaries);
    if (confirmed) backfillSummaries.mutate(null);
  }

  return (
    <div className="rs-legend-page">
      <header className="rs-legend-header">
        <div>
          <div className="rs-eyebrow">{copy.eyebrow}</div>
          <h1 className="rs-display rs-h2">{copy.title}</h1>
        </div>
        <div className="rs-legend-backfill">
          <Button
            onClick={onBackfillSummaries}
            disabled={backfillSummaries.isPending}
          >
            {backfillSummaries.isPending
              ? runsCopy.rerun.queueing
              : copy.backfillSummaries}
          </Button>
          <Button onClick={onRetryEmpty} disabled={retryEmpty.isPending}>
            {retryEmpty.isPending
              ? runsCopy.rerun.queueing
              : copy.retryNoSpeech}
          </Button>
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
          {retryEmpty.isSuccess ? (
            <span className="rs-meta">{copy.retrying(retryEmpty.data)}</span>
          ) : null}
          {backfillSummaries.isSuccess ? (
            <span className="rs-meta">
              {copy.summarising(backfillSummaries.data)}
            </span>
          ) : null}
          {backfillSummaries.isError ? (
            <span className="rs-legend-error">
              {backfillSummaries.error.message}
            </span>
          ) : null}
          {retryEmpty.isError ? (
            <span className="rs-legend-error">{retryEmpty.error.message}</span>
          ) : null}
          {backfill.isError ? (
            <span className="rs-legend-error">{backfill.error.message}</span>
          ) : null}
        </div>
      </header>

      <TranscriptionUsageTable usage={usage.data ?? []} />
      <SummaryUsageTable usage={summaryUsage.data ?? []} />

      <div className="rs-chip-row rs-legend-filters">
        {transcriptionFilters.map(({ value, label }) => (
          <Chip
            key={value}
            active={filter === value}
            onClick={() => setSearch({ filter: value })}
          >
            {label} · {counts[value]}
          </Chip>
        ))}
        <span className="rs-meta rs-legend-hint">{runsCopy.moveHint}</span>
      </div>

      {clipsQuery.isLoading ? (
        <div className="rs-empty">{runsCopy.loadingClips}</div>
      ) : clipsQuery.isError ? (
        <div className="rs-empty">{runsCopy.clipsNotLoaded}</div>
      ) : clips.length === 0 ? (
        <div className="rs-empty">{copy.noClips}</div>
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
            <TranscriptionReviewDetail key={selected.clipId} clip={selected} />
          ) : null}
        </div>
      )}
    </div>
  );
}

const outcomeBadges = {
  transcribed: { tone: "accent", label: copy.outcomes.transcribed },
  silent: { tone: "neutral", label: copy.outcomes.silent },
  "no-audio": { tone: "neutral", label: copy.outcomes.noAudio },
  failed: { tone: "danger", label: copy.outcomes.failed },
  queued: { tone: "neutral", label: copy.outcomes.queued },
  "not-run": { tone: "neutral", label: copy.outcomes.notRun },
} as const;

function ClipRow({
  clip,
  selected,
  onSelect,
}: {
  clip: ClipTranscriptionReviewClip;
  selected: boolean;
  onSelect: () => void;
}) {
  const badge = outcomeBadges[transcriptOutcome(clip.latestRun)];
  return (
    <button
      type="button"
      className={`rs-legend-row${selected ? " selected" : ""}`}
      data-transcription-clip={clip.clipId}
      aria-current={selected ? "true" : undefined}
      onClick={onSelect}
    >
      {clip.thumbnailUrl ? (
        <img
          className="rs-legend-row-thumb"
          src={clip.thumbnailUrl}
          alt=""
          loading="lazy"
        />
      ) : (
        <span className="rs-legend-row-thumb" />
      )}
      <span className="rs-legend-row-text">
        <strong>{clip.title}</strong>
        <span className="rs-meta">
          {clip.ownerName} · {clip.gameName} · {formatDate(clip.createdAt)}
        </span>
      </span>
      <span className="rs-legend-row-status">
        <Badge tone={badge.tone}>{badge.label}</Badge>
      </span>
    </button>
  );
}
