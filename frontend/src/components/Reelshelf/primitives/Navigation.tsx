import { Link, useNavigate } from "@tanstack/react-router";
import {
  IconChevronLeft,
  IconCopy,
  IconDownload,
  IconFolderPlus,
  IconShare3,
  IconTrash,
  IconX,
} from "@tabler/icons-react";
import { useEffect, useMemo, useRef, useState } from "react";
import type { Clip } from "@/api-client";
import { commonCopy } from "@/copy/common";
import { libraryCopy } from "@/copy/library";
import { useCurrentUser, useDeleteClip, useShareClip } from "@/hooks/queries";
import { apiConfig } from "@/shared/config/apiConfig";
import { ApiError } from "@/shared/services/apiError";
import { isHlsSource } from "@/shared/utils/videoStream";
import { AddToCollectionDialog } from "../collections/AddToCollectionDialog";

export function PlayerActions({ clip }: { clip: Clip }) {
  const [shareOpen, setShareOpen] = useState(false);
  const [collectOpen, setCollectOpen] = useState(false);
  const [preparing, setPreparing] = useState(false);
  const navigate = useNavigate();
  const deleteClip = useDeleteClip();
  const { data: currentUser } = useCurrentUser();
  // Only the owner can share or delete; collaborators and share viewers see the clip but not these
  // actions, and the API rejects them for anyone else anyway.
  const isOwner = currentUser?.id === clip.ownerId;
  const canShare = isOwner;
  const canDelete = isOwner;
  const remuxesDownload = clip.video.sources.some(isHlsSource);

  function handleDelete() {
    if (deleteClip.isPending) return;
    const confirmed = window.confirm(
      libraryCopy.actions.confirmDelete(clip.video.title),
    );
    if (!confirmed) return;

    deleteClip.mutate(clip.clipId, {
      onSuccess: () => {
        void navigate({
          to: "/games/$slug",
          params: { slug: clip.categorySlug },
        });
      },
    });
  }

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
        {/* A plain link, never fetch: an R2 clip is a redirect to a presigned URL in the bucket, which
            the browser saves as it is. An HLS (Bunny) clip is remuxed on the server before the file
            starts, which can take a moment; the label says so rather than the button seeming to do
            nothing. */}
        <a
          className="rs-small-button"
          href={`${apiConfig.baseUrl}/api/ffmpeg/download/${clip.videoId}`}
          download
          aria-disabled={preparing}
          onClick={() => {
            if (!remuxesDownload) return;
            setPreparing(true);
            window.setTimeout(() => setPreparing(false), 8000);
          }}
        >
          <IconDownload size={13} />
          {preparing
            ? libraryCopy.actions.preparingDownload
            : libraryCopy.actions.download}
        </a>
        {canDelete ? (
          <button
            className="rs-small-button rs-small-button-danger"
            type="button"
            onClick={handleDelete}
            disabled={deleteClip.isPending}
          >
            <IconTrash size={13} />
            {deleteClip.isPending ? commonCopy.deleting : commonCopy.delete}
          </button>
        ) : null}
      </div>
      {deleteClip.isError ? (
        <div className="rs-upload-error" role="alert">
          {deleteClip.error instanceof Error
            ? deleteClip.error.message
            : libraryCopy.actions.deleteFailed}
        </div>
      ) : null}
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

  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") onClose();
    }

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [onClose]);

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
    <div className="rs-modal-layer" role="presentation" onMouseDown={onClose}>
      <section
        className="rs-modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby="share-clip-title"
        onMouseDown={(event) => event.stopPropagation()}
      >
        <header className="rs-modal-header">
          <div>
            <p className="rs-eyebrow">{libraryCopy.shareDialog.eyebrow}</p>
            <h2 className="rs-modal-title" id="share-clip-title">
              {clip.video.title}
            </h2>
          </div>
          <button
            className="rs-icon-button"
            type="button"
            onClick={onClose}
            aria-label={libraryCopy.shareDialog.close}
          >
            <IconX size={16} />
          </button>
        </header>
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
      </section>
    </div>
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
