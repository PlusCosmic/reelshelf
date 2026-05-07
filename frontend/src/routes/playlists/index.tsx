import { Link, createFileRoute } from "@tanstack/react-router";
import type { CSSProperties } from "react";
import { IconUsers } from "@tabler/icons-react";
import { Avatar, ClipThumb } from "@/components/Reelshelf/ReelshelfPrimitives";
import {
  collectionCoverClips,
  formatDate,
  getGameColors,
} from "@/components/Reelshelf/reelshelf-model";
import {
  useLibraryData,
  usePlaylistsData,
} from "@/components/Reelshelf/useLibraryData";

export const Route = createFileRoute("/playlists/")({
  component: CollectionsRoute,
});

function CollectionsRoute() {
  const { playlists, isLoading, isError } = usePlaylistsData();
  const { clips, categories } = useLibraryData();

  if (isLoading)
    return <div className="rs-section rs-empty">Loading collections...</div>;
  if (isError)
    return (
      <div className="rs-section rs-empty">
        Collections could not be loaded.
      </div>
    );

  return (
    <>
      <section className="rs-hero">
        <div className="rs-eyebrow">
          Collections - {playlists.length} shelves
        </div>
        <h1 className="rs-display rs-h1">
          Curated runs, watch queues, and shared <em>moments</em>.
        </h1>
      </section>
      <section className="rs-section">
        {playlists.length === 0 ? (
          <div className="rs-empty">No collections yet.</div>
        ) : (
          <div className="rs-grid">
            {playlists.map((playlist) => {
              const cover = collectionCoverClips(playlist, clips);
              const [colorA, colorB] = getGameColors(playlist.id);
              return (
                <Link
                  key={playlist.id}
                  to="/playlists/$playlistId"
                  params={{ playlistId: playlist.id }}
                  className="rs-card"
                  style={
                    { "--game-a": colorA, "--game-b": colorB } as CSSProperties
                  }
                >
                  <div
                    className="rs-thumb"
                    style={{
                      display: "grid",
                      gridTemplateColumns: "1fr 1fr",
                      gap: 2,
                      padding: 2,
                    }}
                  >
                    {cover.length > 0 ? (
                      cover.map((clip) => (
                        <ClipThumb
                          key={clip.clipId}
                          clip={clip}
                          category={categories.find(
                            (category) => category.id === clip.gameCategoryId,
                          )}
                          compact
                        />
                      ))
                    ) : (
                      <div
                        className="rs-player-gradient"
                        style={{ position: "absolute", inset: 0 }}
                      />
                    )}
                  </div>
                  <h2 className="rs-card-title" style={{ fontSize: 18 }}>
                    {playlist.name}
                  </h2>
                  <p
                    style={{
                      margin: "0 0 10px",
                      color: "var(--fg-soft)",
                      fontSize: 13,
                    }}
                  >
                    {playlist.description ?? "No description"}
                  </p>
                  <div className="rs-meta">
                    <span>{playlist.clipCount} clips</span>
                    <span className="rs-dot" />
                    <IconUsers size={13} />
                    <span>{playlist.collaboratorCount}</span>
                    <span className="rs-dot" />
                    <span>Updated {formatDate(playlist.updatedAt)}</span>
                  </div>
                  <div style={{ display: "flex", marginTop: 12 }}>
                    {Array.from({
                      length: Math.min(playlist.collaboratorCount || 1, 4),
                    }).map((_, index) => (
                      <span
                        key={index}
                        style={{ marginLeft: index === 0 ? 0 : -6 }}
                      >
                        <Avatar
                          name={index === 0 ? "You" : `Guest ${index}`}
                          size={26}
                        />
                      </span>
                    ))}
                  </div>
                </Link>
              );
            })}
          </div>
        )}
      </section>
    </>
  );
}
