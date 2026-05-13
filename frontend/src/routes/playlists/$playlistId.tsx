import { createFileRoute } from "@tanstack/react-router";
import { useQuery } from "@tanstack/react-query";
import { fetchPlaylistById } from "@/shared/services/playlists";
import {
  Avatar,
  BackToLibrary,
  ClipGrid,
} from "@/components/Reelshelf/ReelshelfPrimitives";
import { formatDate } from "@/components/Reelshelf/reelshelf-model";
import { useLibraryData } from "@/components/Reelshelf/useLibraryData";

export const Route = createFileRoute("/playlists/$playlistId")({
  component: PlaylistRoute,
});

function PlaylistRoute() {
  const { playlistId } = Route.useParams();
  const { categories } = useLibraryData();
  const {
    data: playlist,
    isLoading,
    isError,
  } = useQuery({
    queryKey: ["playlists", playlistId],
    queryFn: () => fetchPlaylistById(playlistId),
  });

  if (isLoading)
    return <div className="rs-section rs-empty">Loading collection…</div>;
  if (isError || !playlist)
    return (
      <div className="rs-section rs-empty">
        This collection could not be loaded.
      </div>
    );

  const clips = playlist.clips
    .flatMap((item) =>
      item.clipDetails
        ? [{ position: item.position, clip: item.clipDetails }]
        : [],
    )
    .toSorted((a, b) => a.position - b.position)
    .map((item) => item.clip);

  return (
    <>
      <section className="rs-hero">
        <BackToLibrary to="/playlists" />
        <div className="rs-eyebrow">
          Collection - {clips.length} clips - Updated{" "}
          {formatDate(playlist.updatedAt)}
        </div>
        <h1 className="rs-display rs-h1">{playlist.name}</h1>
        {playlist.description ? (
          <p className="rs-hero-description">{playlist.description}</p>
        ) : null}
        <div className="rs-collaborator-row">
          {playlist.collaborators.slice(0, 5).map((collaborator, index) => (
            <span
              key={collaborator.userId}
              className={index === 0 ? undefined : "rs-avatar-offset"}
            >
              <Avatar
                name={collaborator.username}
                src={collaborator.avatarUrl}
              />
            </span>
          ))}
          <span className="rs-meta">
            {playlist.collaborators.length} collaborators
          </span>
        </div>
      </section>
      <section className="rs-section">
        <ClipGrid clips={clips} categories={categories} variant="filmstrip" />
      </section>
    </>
  );
}
