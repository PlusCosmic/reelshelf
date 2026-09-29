import { createFileRoute, useNavigate } from "@tanstack/react-router";
import { useEffect, useMemo } from "react";
import { Chip } from "@/components/Reelshelf/ReelshelfPrimitives";
import { formatDate } from "@/components/Reelshelf/reelshelf-model";
import { Badge, Button } from "@/components/ui";
import { TranscriptionReviewDetail } from "@/components/ClipTranscription/TranscriptionReviewDetail";
import { TranscriptionUsageTable } from "@/components/ClipTranscription/TranscriptionUsageTable";
import {
  countByTranscriptionFilter,
  matchesTranscriptionFilter,
  transcriptOutcome,
  transcriptionFilters,
  type TranscriptionFilter,
} from "@/components/ClipTranscription/transcriptionReview";
import {
  useBackfillClipTranscription,
  useCurrentUser,
  useRetryEmptyTranscriptions,
  useTranscriptionReviewClips,
  useTranscriptionUsage,
} from "@/hooks/queries";
import {
  isTranscriptionActive,
  type ClipTranscriptionReviewClip,
} from "@/shared/services/clipTranscription";

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

  if (isLoading) return <div className="rs-section rs-empty">Loading…</div>;
  if (!user?.isAdmin)
    return (
      <div className="rs-section rs-empty">
        Transcription review is only available to admins.
      </div>
    );
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
    const confirmed = window.confirm(
      "Queue a transcription run for every encoded clip of a whitelisted owner that has never had one? Each run is a paid model call.",
    );
    if (confirmed) backfill.mutate();
  }

  function onRetryEmpty() {
    const confirmed = window.confirm(
      "Transcribe again, with the configured model, every clip that came back with no speech from another model? Each run is a paid model call.",
    );
    if (confirmed) retryEmpty.mutate();
  }

  return (
    <div className="rs-legend-page">
      <header className="rs-legend-header">
        <div>
          <div className="rs-eyebrow">Transcription</div>
          <h1 className="rs-display rs-h2">Check what the model heard.</h1>
        </div>
        <div className="rs-legend-backfill">
          <Button onClick={onRetryEmpty} disabled={retryEmpty.isPending}>
            {retryEmpty.isPending ? "Queueing…" : "Retry no-speech clips"}
          </Button>
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
          {retryEmpty.isSuccess ? (
            <span className="rs-meta">
              Retrying {retryEmpty.data}{" "}
              {retryEmpty.data === 1 ? "clip" : "clips"}.
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
        <span className="rs-meta rs-legend-hint">j / k to move</span>
      </div>

      {clipsQuery.isLoading ? (
        <div className="rs-empty">Loading clips…</div>
      ) : clipsQuery.isError ? (
        <div className="rs-empty">Clips could not be loaded.</div>
      ) : clips.length === 0 ? (
        <div className="rs-empty">No whitelisted owner has any clips yet.</div>
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
            <TranscriptionReviewDetail key={selected.clipId} clip={selected} />
          ) : null}
        </div>
      )}
    </div>
  );
}

const outcomeBadges = {
  transcribed: { tone: "accent", label: "Transcribed" },
  silent: { tone: "neutral", label: "No speech" },
  "no-audio": { tone: "neutral", label: "No audio" },
  failed: { tone: "danger", label: "Failed" },
  queued: { tone: "neutral", label: "Queued" },
  "not-run": { tone: "neutral", label: "Not run" },
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
      <img
        className="rs-legend-row-thumb"
        src={clip.thumbnailUrl}
        alt=""
        loading="lazy"
      />
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
