import { useQuery } from "@tanstack/react-query";
import { useMemo } from "react";
import { IconLock } from "@tabler/icons-react";
import { useCurrentUser } from "@/hooks/auth.queries";
import { fetchPlaylistById } from "@/shared/services/playlists";
import {
  collectionClips,
  collectionGames,
  collectionMinutes,
  sharingSummary,
} from "./collections-model";
import { Avatar, BackToLibrary, ClipGrid } from "./ReelshelfPrimitives";
import { formatDate, makeGameShelf } from "./reelshelf-model";
import { useLibraryData } from "./useLibraryData";

function plural(count: number, one: string, many: string) {
  return `${count} ${count === 1 ? one : many}`;
}

/** One collection: who it is shared with, which games it draws on, and its clips in order. */
export function CollectionPage({ playlistId }: { playlistId: string }) {
  const { categories, categoryTotals } = useLibraryData();
  const { data: currentUser } = useCurrentUser();
  const shelf = useMemo(
    () => makeGameShelf(categories, categoryTotals),
    [categories, categoryTotals],
  );
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

  const clips = collectionClips(playlist).map(({ clip }) => clip);
  const games = collectionGames(clips, shelf);
  const sharing = sharingSummary(playlist, currentUser?.id);
  const total = games.reduce((sum, game) => sum + game.count, 0);

  return (
    <main className="rs-ledger-page">
      <BackToLibrary to="/playlists" label="Collections" />
      <header className="rs-collection-head">
        <div>
          <div className="rs-eyebrow">
            {playlist.clips.length > 0
              ? `Collection · ${plural(clips.length, "clip", "clips")} · ${collectionMinutes(clips)} min`
              : "Collection"}{" "}
            · Updated {formatDate(playlist.updatedAt)}
          </div>
          <h1 className="rs-display rs-h1">{playlist.name}</h1>
          {playlist.description ? (
            <p className="rs-collection-description">{playlist.description}</p>
          ) : null}
        </div>
        <div className="rs-ledger-people">
          {sharing.people.length > 0 ? (
            <>
              <span className="rs-avatar-stack">
                {sharing.people.slice(0, 5).map((person, index) => (
                  <span
                    key={person.userId}
                    className={index === 0 ? undefined : "rs-avatar-offset"}
                  >
                    <Avatar
                      name={person.username}
                      src={person.avatarUrl}
                      size={30}
                    />
                  </span>
                ))}
              </span>
              {sharing.label}
            </>
          ) : (
            <>
              <IconLock size={14} aria-hidden="true" />
              Private
            </>
          )}
        </div>
      </header>

      {games.length > 0 ? (
        <div className="rs-collection-share">
          <span
            className="rs-collection-share-bar"
            role="img"
            aria-label={games
              .map(
                (game) =>
                  `${game.name}: ${plural(game.count, "clip", "clips")}`,
              )
              .join(", ")}
          >
            {games.map((game) => (
              <span
                key={game.id}
                style={{
                  width: `${(game.count / total) * 100}%`,
                  background: game.cloth,
                }}
              />
            ))}
          </span>
          <span className="rs-ledger-games" aria-hidden="true">
            {games.map((game) => (
              <span key={game.id}>
                {game.game?.coverUrl ? (
                  <img src={game.game.coverUrl} alt="" loading="lazy" />
                ) : null}
                {game.name} <strong>{game.count}</strong>
              </span>
            ))}
          </span>
        </div>
      ) : null}

      {clips.length > 0 ? (
        <ClipGrid
          clips={clips}
          categories={categories}
          variant="filmstrip"
          showGame={games.length > 1}
        />
      ) : (
        <div className="rs-empty">No clips in this collection yet.</div>
      )}
    </main>
  );
}
