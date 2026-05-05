import { createFileRoute } from "@tanstack/react-router";
import type { CSSProperties } from "react";
import { useEffect, useMemo } from "react";
import { BackToLibrary, Chip, ClipGrid, PlayerActions } from "@/components/Reelshelf/ReelshelfPrimitives";
import {
  categoryForClip,
  formatDate,
  formatSize,
  getGameColors,
  playerUrl,
} from "@/components/Reelshelf/reelshelf-model";
import { useClip, useMarkAsViewed } from "@/hooks/queries";
import { useLibraryData } from "@/components/Reelshelf/useLibraryData";

export const Route = createFileRoute("/games/$slug/$clipId")({
  component: ClipDetailRoute,
});

function ClipDetailRoute() {
  const { slug, clipId } = Route.useParams();
  const { data: clip, isLoading, isError } = useClip(clipId);
  const { categories, clips } = useLibraryData();
  const markViewed = useMarkAsViewed();
  const category = clip ? categoryForClip(clip, categories) : categories.find((item) => item.slug === slug);
  const [colorA, colorB] = getGameColors(category?.id ?? slug);
  const related = useMemo(
    () => clips.filter((item) => item.categorySlug === slug && item.clipId !== clipId).slice(0, 4),
    [clipId, clips, slug],
  );

  useEffect(() => {
    if (clipId) markViewed.mutate(clipId);
  }, [clipId]);

  if (isLoading) return <div className="rs-section rs-empty">Loading player...</div>;
  if (isError || !clip) return <div className="rs-section rs-empty">This clip could not be found.</div>;

  return (
    <div className="rs-player-page" style={{ "--game-a": colorA, "--game-b": colorB } as CSSProperties}>
      <BackToLibrary to={`/games/${slug}`} />

      <div className="rs-player">
        <iframe
          src={playerUrl(clip)}
          loading="lazy"
          title={clip.video.title}
          style={{ position: "absolute", inset: 0, width: "100%", height: "100%", border: 0 }}
          allow="accelerometer; gyroscope; autoplay; encrypted-media; picture-in-picture;"
          allowFullScreen
        />
      </div>

      <div className="rs-player-grid">
        <section>
          <h1 className="rs-display rs-h2">{clip.video.title}</h1>
          <div className="rs-meta" style={{ marginTop: 12 }}>
            <strong style={{ color: "var(--fg)" }}>{category?.name ?? clip.video.category}</strong>
            <span>{formatDate(clip.createdAt)}</span>
            <span>{formatSize(clip.video.storageSize)}</span>
            <span>{clip.isViewed ? "Viewed" : "New"}</span>
          </div>
          <div style={{ display: "flex", gap: 6, flexWrap: "wrap", marginTop: 14 }}>
            {clip.tags.map((tag) => (
              <Chip key={tag}>#{tag}</Chip>
            ))}
          </div>
          <div style={{ marginTop: 18 }}>
            <PlayerActions />
          </div>
        </section>

        <aside className="rs-sidebar-panel">
          <h2 className="rs-eyebrow">More from {category?.name ?? "this game"}</h2>
          <ClipGrid clips={related} categories={categories} variant="grid" />
        </aside>
      </div>
    </div>
  );
}
