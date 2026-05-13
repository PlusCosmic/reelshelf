import { Link, createFileRoute } from "@tanstack/react-router";
import type { CSSProperties } from "react";
import { useQueries } from "@tanstack/react-query";
import { IconLock, IconPlus } from "@tabler/icons-react";
import type { Clip, PlaylistCollaborator } from "@/api-client";
import { Avatar, ClipThumb } from "@/components/Reelshelf/ReelshelfPrimitives";
import { getGameColors } from "@/components/Reelshelf/reelshelf-model";
import {
  useLibraryData,
  usePlaylistsData,
} from "@/components/Reelshelf/useLibraryData";
import { useCurrentUser } from "@/hooks/auth.queries";
import { fetchPlaylistById } from "@/shared/services/playlists";

export const Route = createFileRoute("/playlists/")({
  component: CollectionsRoute,
});

function CollectionsRoute() {
  const { playlists, isLoading, isError } = usePlaylistsData();
  const { categories } = useLibraryData();
  const { data: currentUser } = useCurrentUser();
  const playlistDetails = useQueries({
    queries: playlists.map((playlist) => ({
      queryKey: ["playlists", playlist.id],
      queryFn: () => fetchPlaylistById(playlist.id),
      staleTime: 30_000,
    })),
  });
  const detailsByPlaylistId = new Map(
    playlistDetails.flatMap((query) =>
      query.data ? [[query.data.id, query.data] as const] : [],
    ),
  );

  if (isLoading)
    return <div className="rs-section rs-empty">Loading collections…</div>;
  if (isError)
    return (
      <div className="rs-section rs-empty">
        Collections could not be loaded.
      </div>
    );

  return (
    <>
      <section className="rs-hero rs-collection-hero">
        <div className="rs-collection-hero-row">
          <div>
            <div className="rs-eyebrow">Collections - {playlists.length}</div>
            <h1 className="rs-display rs-h1">
              Curated <em>shelves</em> & shared playlists
            </h1>
          </div>
          <button className="rs-collection-new-button" type="button">
            <IconPlus size={14} aria-hidden="true" />
            New collection
          </button>
        </div>
      </section>
      <section className="rs-section">
        {playlists.length === 0 ? (
          <div className="rs-empty">No collections yet.</div>
        ) : (
          <div className="rs-grid rs-collection-grid">
            {playlists.map((playlist) => {
              const details = detailsByPlaylistId.get(playlist.id);
              const cover = playlistCoverClips(details?.clips ?? []);
              const coverSlotCount = collectionCoverSlotCount(cover.length);
              const [colorA, colorB] = getGameColors(playlist.id);
              const collaborators = details?.collaborators ?? [];
              const visibleCollaborators = collaborators.filter(
                (collaborator) => collaborator.userId !== currentUser?.id,
              );
              return (
                <Link
                  key={playlist.id}
                  to="/playlists/$playlistId"
                  params={{ playlistId: playlist.id }}
                  className="rs-card rs-collection-card"
                  style={
                    { "--game-a": colorA, "--game-b": colorB } as CSSProperties
                  }
                >
                  <div
                    className="rs-collection-cover"
                    data-count={cover.length}
                  >
                    {cover.length > 0 ? (
                      <>
                        {cover.map((clip, index) => (
                          <span
                            key={clip.clipId}
                            className="rs-collection-cover-item"
                            style={{ "--stack-index": index } as CSSProperties}
                          >
                            <ClipThumb
                              clip={clip}
                              category={categories.find(
                                (category) =>
                                  category.id === clip.gameCategoryId,
                              )}
                              compact
                            />
                          </span>
                        ))}
                        {Array.from({
                          length: coverSlotCount - cover.length,
                        }).map((_, index) => (
                          <span
                            key={`empty-${index}`}
                            className="rs-collection-cover-item rs-collection-cover-empty"
                          />
                        ))}
                      </>
                    ) : (
                      <div className="rs-player-gradient rs-absolute-fill" />
                    )}
                  </div>
                  <div className="rs-collection-card-body">
                    <div className="rs-collection-card-title-row">
                      <h2 className="rs-card-title rs-collection-title">
                        {playlist.name}
                      </h2>
                      <span>{formatClipCount(playlist.clipCount)}</span>
                    </div>
                    <p
                      className="rs-card-description"
                      aria-hidden={!playlist.description}
                    >
                      {playlist.description || ""}
                    </p>
                    <div className="rs-collection-collaborators">
                      {visibleCollaborators.length > 0 ? (
                        <>
                          <div className="rs-avatar-stack">
                            {visibleCollaborators
                              .slice(0, 4)
                              .map((collaborator, index) => (
                                <span
                                  key={collaborator.userId}
                                  className={
                                    index === 0 ? undefined : "rs-avatar-offset"
                                  }
                                >
                                  <Avatar
                                    name={collaborator.username}
                                    src={collaborator.avatarUrl}
                                    size={22}
                                  />
                                </span>
                              ))}
                          </div>
                          <span>
                            {collaboratorSummary(visibleCollaborators)}
                          </span>
                        </>
                      ) : (
                        <span className="rs-collection-private">
                          <IconLock size={13} aria-hidden="true" />
                          Private
                        </span>
                      )}
                    </div>
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

function playlistCoverClips(
  clips: { position: number; clipDetails?: Clip | null }[],
) {
  const sorted = clips.toSorted((a, b) => a.position - b.position);
  const cover: Clip[] = [];
  for (const clip of sorted) {
    if (clip.clipDetails) cover.push(clip.clipDetails);
    if (cover.length === 4) break;
  }
  return cover;
}

function collectionCoverSlotCount(clipCount: number) {
  if (clipCount === 3) return 4;
  return clipCount;
}

function formatClipCount(count: number) {
  return `${count} ${count === 1 ? "clip" : "clips"}`;
}

function collaboratorSummary(collaborators: PlaylistCollaborator[]) {
  const names = collaborators.flatMap((collaborator) =>
    collaborator.username ? [collaborator.username] : [],
  );

  if (names.length === 0) return "Shared collection";
  if (names.length === 1) return `Shared with ${names[0]}`;
  if (names.length === 2) return `Shared with ${names[0]}, ${names[1]}`;
  return `Shared with ${names[0]}, ${names[1]} +${names.length - 2}`;
}
