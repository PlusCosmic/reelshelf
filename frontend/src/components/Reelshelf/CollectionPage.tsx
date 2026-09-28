import { useNavigate } from "@tanstack/react-router";
import { useQuery } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import { IconLock, IconPencil, IconUserPlus } from "@tabler/icons-react";
import { useCurrentUser } from "@/hooks/auth.queries";
import {
  useDeleteCollection,
  useRemoveClipFromCollection,
  useReorderCollection,
  useUpdateCollection,
} from "@/hooks/playlists.queries";
import { fetchPlaylistById } from "@/shared/services/playlists";
import {
  collectionClips,
  collectionGames,
  collectionMinutes,
  sharingSummary,
} from "./collections-model";
import { CollectionFormDialog } from "./collections/CollectionFormDialog";
import { CollectionRows } from "./collections/CollectionRows";
import { PeopleDialog } from "./collections/PeopleDialog";
import { Avatar, BackToLibrary } from "./ReelshelfPrimitives";
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
  const navigate = useNavigate();
  const [dialog, setDialog] = useState<"edit" | "people" | null>(null);
  const update = useUpdateCollection(playlistId);
  const remove = useDeleteCollection(playlistId);
  const reorder = useReorderCollection(playlistId);
  const takeOut = useRemoveClipFromCollection();

  if (isLoading)
    return <div className="rs-section rs-empty">Loading collection…</div>;
  if (isError || !playlist)
    return (
      <div className="rs-section rs-empty">
        This collection could not be loaded.
      </div>
    );

  const entries = collectionClips(playlist);
  const clips = entries.map(({ clip }) => clip);
  const isOwner = playlist.creatorUserId === currentUser?.id;
  const games = collectionGames(clips, shelf);
  const sharing = sharingSummary(playlist, currentUser?.id);
  const total = games.reduce((sum, game) => sum + game.count, 0);

  return (
    <main className="rs-ledger-page">
      <BackToLibrary to="/collections" label="Collections" />
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
        <div className="rs-collection-actions">
          <button
            type="button"
            className="rs-ledger-people rs-collection-people"
            onClick={() => setDialog("people")}
            aria-label={`People: ${sharing.label}`}
          >
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
          </button>
          {isOwner ? (
            <>
              <button
                type="button"
                className="rs-small-button"
                onClick={() => setDialog("people")}
              >
                <IconUserPlus size={15} aria-hidden="true" />
                Invite
              </button>
              <button
                type="button"
                className="rs-small-button"
                onClick={() => {
                  update.reset();
                  setDialog("edit");
                }}
              >
                <IconPencil size={15} aria-hidden="true" />
                Edit
              </button>
            </>
          ) : null}
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
        <CollectionRows
          playlist={playlist}
          entries={entries}
          categories={categories}
          showGame={games.length > 1}
          viewerId={currentUser?.id}
          onReorder={(clipIds) => reorder.mutate(clipIds)}
          onRemove={(clip) =>
            takeOut.mutate({ playlistId, clipId: clip.clipId })
          }
          removingId={
            takeOut.isPending ? (takeOut.variables?.clipId ?? null) : null
          }
        />
      ) : (
        <div className="rs-empty">
          No clips in this collection yet. Add them from a clip's page with Add
          to collection.
        </div>
      )}
      {reorder.isError || takeOut.isError ? (
        <p className="rs-upload-error" role="alert">
          That change could not be saved. Try again.
        </p>
      ) : null}

      {dialog === "people" ? (
        <PeopleDialog
          playlist={playlist}
          viewerId={currentUser?.id}
          onClose={() => setDialog(null)}
        />
      ) : null}
      {dialog === "edit" ? (
        <CollectionFormDialog
          title="Edit collection"
          submitLabel="Save"
          savingLabel="Saving…"
          initialName={playlist.name}
          initialDescription={playlist.description ?? ""}
          saving={update.isPending}
          error={update.error ?? remove.error}
          onClose={() => setDialog(null)}
          onSubmit={(name, description) =>
            update.mutate(
              { name, description },
              { onSuccess: () => setDialog(null) },
            )
          }
          deleting={remove.isPending}
          onDelete={() => {
            if (
              !window.confirm(
                `Delete "${playlist.name}"? The clips stay in your library.`,
              )
            )
              return;
            remove.mutate(undefined, {
              onSuccess: () => void navigate({ to: "/collections" }),
            });
          }}
        />
      ) : null}
    </main>
  );
}
