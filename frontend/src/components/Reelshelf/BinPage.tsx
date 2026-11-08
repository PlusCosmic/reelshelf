import { Link } from "@tanstack/react-router";
import {
  IconArrowBackUp,
  IconCheck,
  IconChevronLeft,
  IconTrash,
  IconX,
} from "@tabler/icons-react";
import { useMemo, useState } from "react";
import type { Clip } from "@/api-client";
import { LoadMore } from "@pluscosmic/ui";
import { binCopy } from "@/copy/bin";
import { commonCopy } from "@/copy/common";
import { libraryCopy } from "@/copy/library";
import {
  useClipBin,
  useDeleteBinnedClips,
  useRestoreClips,
} from "@/hooks/bin.queries";
import { binDaysLeft } from "./binModel";
import { useBin } from "./BinToast";
import { chosenCount, isChosen, useClipSelection } from "./clipSelection";
import { Dialog } from "@pluscosmic/ui";
import { ClipThumb } from "./primitives/ClipViews";
import { categoryForClip, formatDate } from "./reelshelf-model";
import { useLibraryData } from "./useLibraryData";

const copy = binCopy.page;

/** What the confirmation is about to delete for good: some clips, or everything in the bin. */
type Deleting = { clipIds: string[] } | "all";

/**
 * The bin: clips their owner deleted, newest in first, each with how long it has left. They can be put back or
 * deleted for good, one at a time, chosen together, or all at once.
 */
export function BinPage() {
  const query = useClipBin();
  const restore = useRestoreClips();
  const deleteForever = useDeleteBinnedClips();
  const { announceRestored } = useBin();
  const { categories } = useLibraryData();
  const [selection, select] = useClipSelection();
  const [confirming, setConfirming] = useState<Deleting | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const clips = useMemo(
    () => query.data?.pages.flatMap((page) => page.clips) ?? [],
    [query.data],
  );
  const order = useMemo(() => clips.map((clip) => clip.clipId), [clips]);
  const total = query.data?.pages[0]?.totalClips ?? 0;
  const retentionDays = query.data?.pages[0]?.retentionDays ?? 30;
  const chosen = chosenCount(selection, total);

  const putBack = (clipIds: string[]) => {
    setNotice(null);
    restore.mutate(clipIds, {
      onSuccess: (result) => {
        announceRestored(result.clipIds.length);
        select({ type: "clear" });
      },
      onError: () => setNotice(copy.restoreFailed),
    });
  };

  const confirmDelete = () => {
    if (!confirming) return;
    setNotice(null);
    deleteForever.mutate(confirming === "all" ? null : confirming.clipIds, {
      onSuccess: (result) => {
        setConfirming(null);
        select({ type: "clear" });
        if (result.held.length > 0) setNotice(copy.held(result.held.length));
      },
      onError: () => {
        setConfirming(null);
        setNotice(copy.deleteFailed);
      },
    });
  };

  const chosenIds = selection.allMatching ? order : [...selection.selected];

  return (
    <main className="rs-bin-page">
      <section className="rs-hero rs-bin-hero">
        <Link to="/" className="rs-bin-back">
          <IconChevronLeft size={16} aria-hidden="true" />
          {copy.backToLibrary}
        </Link>
        <div className="rs-eyebrow">{copy.eyebrow}</div>
        <h1 className="rs-display rs-h1">{copy.title}</h1>
        <p className="rs-bin-intro">{copy.intro(retentionDays)}</p>
        {total > 0 ? (
          <div className="rs-bin-head-actions">
            <span className="rs-bin-count">{copy.count(total)}</span>
            <button
              type="button"
              className="rs-small-button rs-small-button-danger"
              onClick={() => setConfirming("all")}
              disabled={deleteForever.isPending}
            >
              <IconTrash size={13} aria-hidden="true" />
              {copy.emptyBin}
            </button>
          </div>
        ) : null}
      </section>

      <section className="rs-section rs-bin-list">
        {notice ? (
          <p className="rs-linked-notice" data-tone="error" role="alert">
            {notice}
          </p>
        ) : null}
        {query.isLoading ? (
          <div className="rs-empty">{copy.loading}</div>
        ) : query.isError && clips.length === 0 ? (
          <div className="rs-empty">{copy.loadFailed}</div>
        ) : clips.length === 0 ? (
          <div className="rs-empty">{copy.empty}</div>
        ) : (
          <>
            <ul className="rs-bin-rows">
              {clips.map((clip) => (
                <BinRow
                  key={clip.clipId}
                  clip={clip}
                  category={categoryForClip(clip, categories)}
                  retentionDays={retentionDays}
                  chosen={isChosen(selection, clip.clipId)}
                  onToggle={(range) =>
                    select({
                      type: "toggle",
                      clipId: clip.clipId,
                      range,
                      order,
                    })
                  }
                  onRestore={() => putBack([clip.clipId])}
                  onDelete={() => setConfirming({ clipIds: [clip.clipId] })}
                  busy={restore.isPending || deleteForever.isPending}
                />
              ))}
            </ul>
            <LoadMore label={libraryCopy.clipsNoun} query={query} />
          </>
        )}
      </section>

      {selection.active && chosen > 0 ? (
        <div
          className="rs-selection-bar"
          role="region"
          aria-label={binCopy.selection.barLabel}
        >
          <div className="rs-selection-count" aria-live="polite">
            <strong>{binCopy.selection.selected(chosen)}</strong>
          </div>
          <div className="rs-selection-actions">
            <button
              type="button"
              className="rs-small-button"
              onClick={() => select({ type: "clear" })}
            >
              <IconX size={13} aria-hidden="true" />
              {binCopy.selection.clear}
            </button>
            <button
              type="button"
              className="rs-small-button"
              onClick={() => putBack(chosenIds)}
              disabled={restore.isPending}
            >
              <IconArrowBackUp size={13} aria-hidden="true" />
              {restore.isPending
                ? copy.restoring
                : copy.restoreSelected(chosen)}
            </button>
            <button
              type="button"
              className="rs-small-button rs-selection-bin"
              onClick={() => setConfirming({ clipIds: chosenIds })}
              disabled={deleteForever.isPending}
            >
              <IconTrash size={13} aria-hidden="true" />
              {copy.deleteSelected(chosen)}
            </button>
          </div>
        </div>
      ) : null}

      {confirming ? (
        <Dialog
          title={copy.confirm.title(
            confirming === "all" ? "all" : confirming.clipIds.length,
          )}
          busy={deleteForever.isPending}
          onClose={() => setConfirming(null)}
        >
          {() => (
            <div className="rs-modal-panel">
              <p className="rs-modal-note">
                {copy.confirm.body(
                  confirming === "all" ? total : confirming.clipIds.length,
                )}
              </p>
              <div className="rs-modal-actions">
                <button
                  className="rs-secondary"
                  type="button"
                  onClick={() => setConfirming(null)}
                  disabled={deleteForever.isPending}
                >
                  {commonCopy.cancel}
                </button>
                <button
                  className="rs-primary rs-primary-danger"
                  type="button"
                  onClick={confirmDelete}
                  disabled={deleteForever.isPending}
                  autoFocus
                >
                  {deleteForever.isPending
                    ? copy.confirm.deleting
                    : copy.confirm.confirm}
                </button>
              </div>
            </div>
          )}
        </Dialog>
      ) : null}
    </main>
  );
}

function BinRow({
  clip,
  category,
  retentionDays,
  chosen,
  onToggle,
  onRestore,
  onDelete,
  busy,
}: {
  clip: Clip;
  category: ReturnType<typeof categoryForClip>;
  retentionDays: number;
  chosen: boolean;
  onToggle: (range: boolean) => void;
  onRestore: () => void;
  onDelete: () => void;
  busy: boolean;
}) {
  const binnedAt = clip.binnedAt ? new Date(clip.binnedAt) : new Date();
  return (
    <li className={`rs-bin-row${chosen ? " chosen" : ""}`}>
      <button
        type="button"
        role="checkbox"
        aria-checked={chosen}
        className="rs-row-check"
        aria-label={copy.select(clip.video.title)}
        onClick={(event) => onToggle(event.shiftKey)}
      >
        <IconCheck size={14} aria-hidden="true" />
      </button>
      <Link
        to="/games/$slug/$clipId"
        params={{ slug: clip.categorySlug, clipId: clip.clipId }}
        className="rs-bin-row-clip"
      >
        <ClipThumb clip={clip} category={category} compact />
        <span className="rs-row-body">
          <strong className="rs-row-title">{clip.video.title}</strong>
          <span className="rs-meta">
            {category?.name ?? clip.categorySlug} ·{" "}
            {copy.binnedOn(formatDate(binnedAt))}
          </span>
        </span>
      </Link>
      <span className="rs-bin-row-left rs-meta">
        {copy.deletesIn(binDaysLeft(binnedAt, retentionDays))}
      </span>
      <span className="rs-bin-row-actions">
        <button
          type="button"
          className="rs-small-button"
          onClick={onRestore}
          disabled={busy}
        >
          <IconArrowBackUp size={13} aria-hidden="true" />
          {copy.restore}
        </button>
        <button
          type="button"
          className="rs-small-button rs-small-button-danger"
          onClick={onDelete}
          disabled={busy}
        >
          <IconTrash size={13} aria-hidden="true" />
          {copy.deleteNow}
        </button>
      </span>
    </li>
  );
}
