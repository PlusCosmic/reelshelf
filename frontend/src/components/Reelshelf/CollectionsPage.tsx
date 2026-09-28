import { Link, useNavigate } from "@tanstack/react-router";
import { useQueries } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import { IconLock, IconPlus } from "@tabler/icons-react";
import type { PlaylistSummary, PlaylistWithDetails } from "@/api-client";
import { useCurrentUser } from "@/hooks/auth.queries";
import { useCreateCollection } from "@/hooks/playlists.queries";
import { fetchPlaylistById } from "@/shared/services/playlists";
import {
  collectionClips,
  collectionGames,
  collectionMinutes,
  sharingSummary,
} from "./collections-model";
import { CollectionFormDialog } from "./collections/CollectionFormDialog";
import { Avatar, SearchBox } from "./ReelshelfPrimitives";
import {
  formatDate,
  makeGameShelf,
  thumbnailUrl,
  type GameShelfItem,
} from "./reelshelf-model";
import { useLibraryData, usePlaylistsData } from "./useLibraryData";

function plural(count: number, one: string, many: string) {
  return `${count} ${count === 1 ? one : many}`;
}

/** Up to four of the collection's clips, in its order, over a strip of each game's share. */
function Mosaic({
  details,
  shelf,
}: {
  details: PlaylistWithDetails | undefined;
  shelf: GameShelfItem[];
}) {
  const clips = collectionClips(details).map(({ clip }) => clip);
  const games = collectionGames(clips, shelf);
  const shown = clips.slice(0, 4);

  return (
    <span
      className={`rs-ledger-mosaic${clips.length === 0 ? " empty" : ""}`}
      data-count={shown.length}
      aria-hidden="true"
    >
      {details && clips.length === 0 ? <span>Empty</span> : null}
      {shown.map((clip) => (
        <img key={clip.clipId} src={thumbnailUrl(clip)} alt="" loading="lazy" />
      ))}
      {games.length > 0 ? (
        <span className="rs-ledger-share">
          {games.map((game) => (
            <span
              key={game.id}
              style={{ flexGrow: game.count, background: game.cloth }}
            />
          ))}
        </span>
      ) : null}
    </span>
  );
}

function LedgerRow({
  playlist,
  details,
  shelf,
  viewerId,
}: {
  playlist: PlaylistSummary;
  details: PlaylistWithDetails | undefined;
  shelf: GameShelfItem[];
  viewerId: string | undefined;
}) {
  const clips = collectionClips(details).map(({ clip }) => clip);
  const games = collectionGames(clips, shelf);
  const sharing = details ? sharingSummary(details, viewerId) : null;
  const clipCount =
    playlist.clipCount === 0
      ? "No clips yet"
      : plural(playlist.clipCount, "clip", "clips");

  return (
    <Link
      to="/collections/$playlistId"
      params={{ playlistId: playlist.id }}
      className="rs-ledger-row"
    >
      <Mosaic details={details} shelf={shelf} />
      <span className="rs-ledger-body">
        <span className="rs-ledger-name">
          <span className="rs-display">{playlist.name}</span>
        </span>
        {playlist.description ? (
          <span className="rs-ledger-description">{playlist.description}</span>
        ) : null}
        {games.length > 0 ? (
          <span className="rs-ledger-games">
            {games.slice(0, 3).map((game) => (
              <span key={game.id}>
                {game.game?.coverUrl ? (
                  <img src={game.game.coverUrl} alt="" loading="lazy" />
                ) : null}
                {game.name} <strong>{game.count}</strong>
              </span>
            ))}
          </span>
        ) : null}
        <span className="rs-ledger-phone-meta">
          <strong>{clipCount}</strong> · {formatDate(playlist.updatedAt)}
        </span>
      </span>
      <span className="rs-ledger-people">
        {sharing ? (
          sharing.people.length > 0 ? (
            <>
              <span className="rs-avatar-stack">
                {sharing.people.slice(0, 3).map((person, index) => (
                  <span
                    key={person.userId}
                    className={index === 0 ? undefined : "rs-avatar-offset"}
                  >
                    <Avatar
                      name={person.username}
                      src={person.avatarUrl}
                      size={24}
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
          )
        ) : null}
      </span>
      <span className="rs-ledger-count">
        <strong>{clipCount}</strong>
        {clips.length > 0 ? <span>{collectionMinutes(clips)} min</span> : null}
      </span>
      <span className="rs-ledger-updated">
        <small>Updated</small>
        {formatDate(playlist.updatedAt)}
      </span>
    </Link>
  );
}

/**
 * Collections as a ledger: one dense row each, grouped into the ones you made, the ones friends
 * shared with you, and the sessions Reelshelf made from a night of play.
 */
export function CollectionsPage() {
  const { playlists, isLoading, isError } = usePlaylistsData();
  const { categories, categoryTotals } = useLibraryData();
  const { data: currentUser } = useCurrentUser();
  const [query, setQuery] = useState("");
  const [creating, setCreating] = useState(false);
  const createCollection = useCreateCollection();
  const navigate = useNavigate();
  const shelf = useMemo(
    () => makeGameShelf(categories, categoryTotals),
    [categories, categoryTotals],
  );
  const detailQueries = useQueries({
    queries: playlists.map((playlist) => ({
      queryKey: ["playlists", playlist.id],
      queryFn: () => fetchPlaylistById(playlist.id),
      staleTime: 30_000,
    })),
  });
  const detailsById = new Map(
    detailQueries.flatMap((detail) =>
      detail.data ? [[detail.data.id, detail.data] as const] : [],
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

  const needle = query.trim().toLowerCase();
  const matching = needle
    ? playlists.filter((playlist) =>
        `${playlist.name} ${playlist.description ?? ""}`
          .toLowerCase()
          .includes(needle),
      )
    : playlists;
  const groups = [
    {
      key: "mine",
      title: "Made by you",
      note: null,
      items: matching.filter(
        (playlist) =>
          !playlist.isGamingSession &&
          playlist.creatorUserId === currentUser?.id,
      ),
    },
    {
      key: "shared",
      title: "Shared with you",
      note: "Collections friends invited you to",
      items: matching.filter(
        (playlist) =>
          !playlist.isGamingSession &&
          playlist.creatorUserId !== currentUser?.id,
      ),
    },
    {
      key: "sessions",
      title: "Sessions",
      note: "Made for you from each night of play",
      items: matching.filter((playlist) => playlist.isGamingSession),
    },
  ].filter((group) => group.items.length > 0);

  return (
    <main className="rs-ledger-page">
      <header className="rs-ledger-head">
        <div>
          <div className="rs-eyebrow">Collections · {playlists.length}</div>
          <h1 className="rs-display rs-h1">Collections</h1>
        </div>
        <div className="rs-ledger-tools">
          {playlists.length > 0 ? (
            <SearchBox
              value={query}
              onChange={setQuery}
              placeholder="Search collections"
            />
          ) : null}
          <button
            className="rs-collection-new-button"
            type="button"
            onClick={() => {
              createCollection.reset();
              setCreating(true);
            }}
          >
            <IconPlus size={16} aria-hidden="true" />
            <span>New collection</span>
          </button>
        </div>
      </header>

      {playlists.length === 0 ? (
        <div className="rs-empty">No collections yet.</div>
      ) : groups.length === 0 ? (
        <div className="rs-empty">No collections match “{query.trim()}”.</div>
      ) : (
        groups.map((group) => (
          <section
            key={group.key}
            className="rs-ledger-group"
            aria-labelledby={`rs-ledger-${group.key}`}
          >
            <div>
              <h2 id={`rs-ledger-${group.key}`} className="rs-ledger-heading">
                <span className="rs-display">{group.title}</span>
                <span className="rs-ledger-heading-count">
                  {group.items.length}
                </span>
              </h2>
              {group.note ? (
                <p className="rs-ledger-heading-note">{group.note}</p>
              ) : null}
            </div>
            {group.items.map((playlist) => (
              <LedgerRow
                key={playlist.id}
                playlist={playlist}
                details={detailsById.get(playlist.id)}
                shelf={shelf}
                viewerId={currentUser?.id}
              />
            ))}
          </section>
        ))
      )}
      {creating ? (
        <CollectionFormDialog
          title="New collection"
          submitLabel="Make collection"
          savingLabel="Making…"
          saving={createCollection.isPending}
          error={createCollection.error}
          onClose={() => setCreating(false)}
          onSubmit={(name, description) =>
            createCollection.mutate(
              { name, description },
              {
                onSuccess: (playlist) =>
                  void navigate({
                    to: "/collections/$playlistId",
                    params: { playlistId: playlist.id },
                  }),
              },
            )
          }
        />
      ) : null}
    </main>
  );
}
