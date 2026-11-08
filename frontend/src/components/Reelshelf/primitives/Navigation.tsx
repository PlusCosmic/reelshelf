import { Link } from "@tanstack/react-router";
import {
  IconChevronLeft,
  IconCopy,
  IconDownload,
  IconFolderPlus,
  IconShare3,
  IconTrash,
} from "@tabler/icons-react";
import { useEffect, useMemo, useRef, useState } from "react";
import { Dialog } from "@pluscosmic/ui";
import type { Clip } from "@/api-client";
import { libraryCopy } from "@/copy/library";
import { useCurrentUser, useShareClip } from "@/hooks/queries";
import { apiConfig } from "@/shared/config/apiConfig";
import { ApiError } from "@/shared/services/apiError";
import { AddToCollectionDialog } from "../collections/AddToCollectionDialog";

/**
 * The clip page's actions. Deleting moves the clip to the bin with no question asked, since it can be undone; the page
 * says what happens next (`onBin`: the next clip in its list, or back to the list).
 */
export function PlayerActions({
  clip,
  onBin,
  binning = false,
}: {
  clip: Clip;
  onBin?: () => void;
  binning?: boolean;
}) {
  const [shareOpen, setShareOpen] = useState(false);
  const [collectOpen, setCollectOpen] = useState(false);
  const { data: currentUser } = useCurrentUser();
  // Only the owner can share or delete; collaborators and share viewers see the clip but not these
  // actions, and the API rejects them for anyone else anyway.
  const isOwner = currentUser?.id === clip.ownerId;
  const canShare = isOwner;
  // A clip already in the bin is put back from the note above the player instead.
  const canDelete = isOwner && !!onBin && !clip.binnedAt;

  return (
    <>
      <div className="rs-action-row">
        {canShare ? (
          <button
            className="rs-small-button"
            type="button"
            onClick={() => setShareOpen(true)}
          >
            <IconShare3 size={13} />
            {libraryCopy.actions.share}
          </button>
        ) : null}
        <button
          className="rs-small-button"
          type="button"
          onClick={() => setCollectOpen(true)}
        >
          <IconFolderPlus size={13} />
          {libraryCopy.actions.addToCollection}
        </button>
        {/* A plain link, never fetch: the download is a redirect to a presigned URL in the bucket, which
            the browser saves as it is. */}
        <a
          className="rs-small-button"
          href={`${apiConfig.baseUrl}/api/ffmpeg/download/${clip.videoId}`}
          download
        >
          <IconDownload size={13} />
          {libraryCopy.actions.download}
        </a>
        {canDelete ? (
          <button
            className="rs-small-button rs-small-button-danger"
            type="button"
            onClick={onBin}
            disabled={binning}
          >
            <IconTrash size={13} />
            {binning
              ? libraryCopy.actions.moving
              : libraryCopy.actions.moveToBin}
            <kbd>{libraryCopy.actions.moveToBinKey}</kbd>
          </button>
        ) : null}
      </div>
      {shareOpen ? (
        <ShareClipDialog clip={clip} onClose={() => setShareOpen(false)} />
      ) : null}
      {collectOpen ? (
        <AddToCollectionDialog
          clip={clip}
          onClose={() => setCollectOpen(false)}
        />
      ) : null}
    </>
  );
}

function ShareClipDialog({
  clip,
  onClose,
}: {
  clip: Clip;
  onClose: () => void;
}) {
  const shareClip = useShareClip();
  const [copied, setCopied] = useState(false);
  const inputRef = useRef<HTMLInputElement | null>(null);

  useEffect(() => {
    shareClip.mutate(clip.clipId);
  }, [clip.clipId]);

  const shareUrl = useMemo(() => {
    if (!shareClip.data?.sharePath) return "";
    return new URL(shareClip.data.sharePath, window.location.origin).toString();
  }, [shareClip.data?.sharePath]);

  const errorMessage =
    shareClip.error instanceof ApiError && shareClip.error.kind === "conflict"
      ? libraryCopy.shareDialog.stillProcessing
      : libraryCopy.shareDialog.failed;

  async function copyShareUrl() {
    if (!shareUrl) return;

    try {
      await navigator.clipboard.writeText(shareUrl);
      setCopied(true);
    } catch {
      inputRef.current?.focus();
      inputRef.current?.select();
      setCopied(false);
    }
  }

  return (
    <Dialog
      eyebrow={libraryCopy.shareDialog.eyebrow}
      title={clip.video.title}
      onClose={onClose}
    >
      <div className="rs-modal-panel">
        {shareClip.isPending ? (
          <p className="rs-modal-note rs-inline-status">
            <span className="rs-spinner" aria-hidden="true" />
            {libraryCopy.shareDialog.creating}
          </p>
        ) : null}

        {shareClip.isError ? (
          <>
            <p className="rs-modal-error rs-modal-error-inline">
              {errorMessage}
            </p>
            <button
              className="rs-secondary rs-modal-submit"
              type="button"
              onClick={() => shareClip.mutate(clip.clipId)}
            >
              {libraryCopy.shareDialog.retry}
            </button>
          </>
        ) : null}

        {shareUrl ? (
          <>
            <label className="rs-field" htmlFor="share-url">
              {libraryCopy.shareDialog.link}
              <div className="rs-share-copy-row">
                <input
                  ref={inputRef}
                  id="share-url"
                  className="rs-share-url"
                  value={shareUrl}
                  readOnly
                  onFocus={(event) => event.currentTarget.select()}
                />
                <button
                  className="rs-primary"
                  type="button"
                  onClick={copyShareUrl}
                >
                  <IconCopy size={15} />
                  {copied
                    ? libraryCopy.shareDialog.copied
                    : libraryCopy.shareDialog.copy}
                </button>
              </div>
            </label>
            <p className="rs-modal-note">{libraryCopy.shareDialog.note}</p>
          </>
        ) : null}
      </div>
    </Dialog>
  );
}

export function SharedIndicator({ compact = false }: { compact?: boolean }) {
  if (compact) {
    return (
      <span
        className="rs-shared-indicator compact"
        title={libraryCopy.shared}
        aria-label={libraryCopy.shared}
      >
        <IconShare3 size={13} />
      </span>
    );
  }

  return (
    <span className="rs-shared-indicator">
      <IconShare3 size={13} />
      {libraryCopy.shared}
    </span>
  );
}

export function BackToLibrary({
  to = "/",
  label = libraryCopy.backToLibrary,
}: {
  to?: string;
  label?: string;
}) {
  return (
    <Link to={to} className="rs-small-button rs-back-link">
      <IconChevronLeft size={14} />
      {label}
    </Link>
  );
}
