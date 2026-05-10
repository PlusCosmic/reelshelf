import { Link } from "@tanstack/react-router";
import {
  IconChevronLeft,
  IconClock,
  IconCopy,
  IconDownload,
  IconEye,
  IconFolderPlus,
  IconPlus,
  IconShare3,
  IconX,
} from "@tabler/icons-react";
import { useEffect, useMemo, useRef, useState } from "react";
import type { Clip } from "@/api-client";
import { useShareClip } from "@/hooks/queries";
import { ApiError } from "@/shared/services/apiError";

export function PlayerActions({ clip }: { clip: Clip }) {
  const [shareOpen, setShareOpen] = useState(false);

  return (
    <>
      <div className="rs-action-row">
        <button
          className="rs-small-button"
          type="button"
          onClick={() => setShareOpen(true)}
        >
          <IconShare3 size={13} />
          Share
        </button>
        <button className="rs-small-button" type="button">
          <IconFolderPlus size={13} />
          Add to collection
        </button>
        <button className="rs-small-button" type="button">
          <IconDownload size={13} />
          Download
        </button>
      </div>
      {shareOpen ? (
        <ShareClipDialog clip={clip} onClose={() => setShareOpen(false)} />
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
      ? "This clip is still processing and cannot be shared yet."
      : "The share link could not be created. Try again.";

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
            <p className="rs-eyebrow">Share clip</p>
            <h2 className="rs-modal-title" id="share-clip-title">
              {clip.video.title}
            </h2>
          </div>
          <button
            className="rs-icon-button"
            type="button"
            onClick={onClose}
            aria-label="Close share dialog"
          >
            <IconX size={16} />
          </button>
        </header>
        <div className="rs-modal-panel">
          {shareClip.isPending ? (
            <p className="rs-modal-note rs-inline-status">
              <span className="rs-spinner" aria-hidden="true" />
              Creating share link...
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
                Retry
              </button>
            </>
          ) : null}

          {shareUrl ? (
            <>
              <label className="rs-field" htmlFor="share-url">
                Share link
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
                    {copied ? "Copied" : "Copy"}
                  </button>
                </div>
              </label>
              <p className="rs-modal-note">
                Anyone with this link can watch the shared clip.
              </p>
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
        title="Shared"
        aria-label="Shared"
      >
        <IconShare3 size={13} />
      </span>
    );
  }

  return (
    <span className="rs-shared-indicator">
      <IconShare3 size={13} />
      Shared
    </span>
  );
}

export function BackToLibrary({ to = "/" }: { to?: string }) {
  return (
    <Link to={to} className="rs-small-button rs-back-link">
      <IconChevronLeft size={14} />
      Library
    </Link>
  );
}

export const Icons = {
  eye: IconEye,
  clock: IconClock,
  plus: IconPlus,
};
