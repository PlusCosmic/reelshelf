import { createFileRoute } from "@tanstack/react-router";
import { useQuery } from "@tanstack/react-query";
import { fetchPlaylistById } from "@repo/shared/services/playlists";
import { Avatar, BackToLibrary, ClipGrid } from "@/components/Reelshelf/ReelshelfPrimitives";
import { formatDate } from "@/components/Reelshelf/reelshelf-model";
import { useLibraryData } from "@/components/Reelshelf/useLibraryData";

export const Route = createFileRoute("/playlists/$playlistId")({
  component: PlaylistRoute,
});

function PlaylistRoute() {
  const { playlistId } = Route.useParams();
  const { categories } = useLibraryData();
  const { data: playlist, isLoading, isError } = useQuery({
    queryKey: ["playlists", playlistId],
    queryFn: () => fetchPlaylistById(playlistId),
  });

  if (isLoading) return <div className="rs-section rs-empty">Loading collection...</div>;
  if (isError || !playlist) return <div className="rs-section rs-empty">This collection could not be loaded.</div>;

  const clips = playlist.clips
    .slice()
    .sort((a, b) => a.position - b.position)
    .map((item) => item.clipDetails)
    .filter((clip): clip is NonNullable<typeof clip> => Boolean(clip));

  return (
    <>
      <section className="rs-hero">
        <BackToLibrary to="/playlists" />
        <div className="rs-eyebrow">
          Collection - {clips.length} clips - Updated {formatDate(playlist.updatedAt)}
        </div>
        <h1 className="rs-display rs-h1">{playlist.name}</h1>
        {playlist.description ? <p style={{ maxWidth: 680, color: "var(--fg-soft)", fontSize: 15 }}>{playlist.description}</p> : null}
        <div style={{ display: "flex", alignItems: "center", gap: 8, marginTop: 20 }}>
          {playlist.collaborators.slice(0, 5).map((collaborator, index) => (
            <span key={collaborator.userId} style={{ marginLeft: index === 0 ? 0 : -6 }}>
              <Avatar name={collaborator.userId} />
            </span>
          ))}
          <span className="rs-meta">{playlist.collaborators.length} collaborators</span>
        </div>
      </section>
      <section className="rs-section">
        <ClipGrid clips={clips} categories={categories} variant="filmstrip" />
      </section>
    </>
  );
}
