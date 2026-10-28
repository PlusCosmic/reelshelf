import { Link, useNavigate } from "@tanstack/react-router";
import { useQuery } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import {
  IconLock,
  IconPencil,
  IconPlayerPlayFilled,
  IconUserPlus,
} from "@tabler/icons-react";
import { collectionsCopy } from "@/copy/collections";
import { commonCopy } from "@/copy/common";
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
    return (
      <div className="rs-section rs-empty">
        {collectionsCopy.collection.loading}
      </div>
    );
  if (isError || !playlist)
    return (
      <div className="rs-section rs-empty">
        {collectionsCopy.collection.loadFailed}
      </div>
    );

  const entries = collectionClips(playlist);
  const clips = entries.map(({ clip }) => clip);
  const isOwner = playlist.creatorUserId === currentUser?.id;
  const games = collectionGames(clips, shelf);
  const sharing = sharingSummary(
    playlist.creatorUserId,
    playlist.collaborators,
    currentUser?.id,
  );
  const total = games.reduce((sum, game) => sum + game.count, 0);

  return (
    <main className="rs-ledger-page">
      <BackToLibrary
        to="/collections"
        label={collectionsCopy.collection.back}
      />
      <header className="rs-collection-head">
        <div>
          <div className="rs-eyebrow">
            {collectionsCopy.collection.eyebrow(
              playlist.clips.length > 0
                ? { clips: clips.length, minutes: collectionMinutes(clips) }
                : null,
              formatDate(playlist.updatedAt),
            )}
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
            aria-label={collectionsCopy.collection.people(sharing.label)}
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
                {collectionsCopy.private}
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
                {collectionsCopy.collection.invite}
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
                {collectionsCopy.collection.edit}
              </button>
            </>
          ) : null}
          {clips.length > 0 ? (
            <Link
              className="rs-collection-play"
              to="/games/$slug/$clipId"
              params={{ slug: clips[0].categorySlug, clipId: clips[0].clipId }}
              search={{ collection: playlist.id }}
            >
              <IconPlayerPlayFilled size={15} aria-hidden="true" />
              {collectionsCopy.collection.playInOrder}
            </Link>
          ) : null}
        </div>
      </header>

      {games.length > 0 ? (
        <div className="rs-collection-share">
          <span
            className="rs-collection-share-bar"
            role="img"
            aria-label={games
              .map((game) =>
                collectionsCopy.collection.gameShare(game.name, game.count),
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
        <div className="rs-empty">{collectionsCopy.collection.empty}</div>
      )}
      {reorder.isError || takeOut.isError ? (
        <p className="rs-upload-error" role="alert">
          {collectionsCopy.changeFailed}
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
          title={collectionsCopy.collection.editTitle}
          submitLabel={commonCopy.save}
          savingLabel={commonCopy.saving}
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
                collectionsCopy.collection.confirmDelete(playlist.name),
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
