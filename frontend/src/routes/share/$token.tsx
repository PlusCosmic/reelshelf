import { Link, createFileRoute } from "@tanstack/react-router";
import { IconMovie } from "@tabler/icons-react";
import { useEffect } from "react";
import { useSharedClip } from "@/hooks/queries";
import { ApiError } from "@/shared/services/apiError";
import {
  formatDate,
  formatDuration,
} from "@/components/Reelshelf/reelshelf-model";

export const Route = createFileRoute("/share/$token")({
  component: SharedClipRoute,
});

function SharedClipRoute() {
  const { token } = Route.useParams();
  const { data: clip, isLoading, isError, error } = useSharedClip(token);

  useEffect(() => {
    const existing = document.querySelector<HTMLMetaElement>(
      'meta[name="robots"]',
    );
    const meta = existing ?? document.createElement("meta");
    meta.name = "robots";
    meta.content = "noindex,nofollow";
    if (!existing) document.head.appendChild(meta);

    return () => {
      if (!existing) meta.remove();
    };
  }, []);

  return (
    <main className="rs-public-page">
      <header className="rs-public-header">
        <Link to="/" className="rs-brand" aria-label="Reelshelf">
          <span className="rs-brand-mark">
            <IconMovie size={16} stroke={2.2} />
          </span>
          <span className="rs-brand-name">
            Reel<em>shelf</em>
          </span>
        </Link>
      </header>

      {isLoading ? (
        <section className="rs-public-state" aria-live="polite">
          <span className="rs-spinner" aria-hidden="true" />
          <p>Loading shared clip...</p>
        </section>
      ) : null}

      {isError ? <SharedClipError error={error} /> : null}

      {clip ? (
        <section className="rs-public-player-wrap">
          <div className="rs-player rs-public-player">
            <iframe
              src={clip.embedUrl}
              loading="lazy"
              title={clip.title}
              className="rs-player-frame"
              allow="accelerometer; gyroscope; autoplay; encrypted-media; picture-in-picture;"
              allowFullScreen
            />
          </div>
          <div className="rs-public-details">
            <h1 className="rs-display rs-h2">{clip.title}</h1>
            <div className="rs-meta rs-player-meta">
              <strong>{clip.game}</strong>
              <span>{formatDuration(clip.durationSeconds)}</span>
              <span>{formatDate(clip.uploadedAt)}</span>
            </div>
          </div>
        </section>
      ) : null}
    </main>
  );
}

function SharedClipError({ error }: { error: unknown }) {
  const message =
    error instanceof ApiError && error.kind === "not-found"
      ? "This shared clip is unavailable."
      : "Shared clip could not be loaded. Try again later.";

  return (
    <section className="rs-public-state" aria-live="polite">
      <h1 className="rs-display rs-h2">{message}</h1>
    </section>
  );
}
