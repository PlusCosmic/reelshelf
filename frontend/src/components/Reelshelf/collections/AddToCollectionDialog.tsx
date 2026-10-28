import { useQueries } from "@tanstack/react-query";
import type { FormEvent } from "react";
import { useState } from "react";
import { IconCheck, IconPlus } from "@tabler/icons-react";
import type { Clip } from "@/api-client";
import { collectionsCopy } from "@/copy/collections";
import { commonCopy } from "@/copy/common";
import {
  useAddClipToCollection,
  useCreateCollection,
  useRemoveClipFromCollection,
} from "@/hooks/playlists.queries";
import { fetchPlaylistById } from "@/shared/services/playlists";
import { Dialog } from "../Dialog";
import { usePlaylistsData } from "../useLibraryData";

/**
 * Put a clip in any of the viewer's collections, or take it out, or start a new collection with it.
 * Sessions are left out: they record a night of play rather than being built by hand.
 */
export function AddToCollectionDialog({
  clip,
  onClose,
}: {
  clip: Clip;
  onClose: () => void;
}) {
  const { playlists, isLoading } = usePlaylistsData();
  const collections = playlists.filter((playlist) => !playlist.isGamingSession);
  const details = useQueries({
    queries: collections.map((playlist) => ({
      queryKey: ["playlists", playlist.id],
      queryFn: () => fetchPlaylistById(playlist.id),
      staleTime: 30_000,
    })),
  });
  const add = useAddClipToCollection();
  const remove = useRemoveClipFromCollection();
  const create = useCreateCollection();
  const [making, setMaking] = useState(false);
  const [name, setName] = useState("");
  const busy = add.isPending || remove.isPending || create.isPending;
  const error = add.error ?? remove.error ?? create.error;

  const holds = (index: number) =>
    details[index]?.data?.clips.some((item) => item.clipId === clip.clipId);

  const toggle = (playlistId: string, has: boolean) => {
    const target = { playlistId, clipId: clip.clipId };
    if (has) remove.mutate(target);
    else add.mutate(target);
  };

  const makeWithClip = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!name.trim()) return;
    create.mutate(
      { name: name.trim(), description: null },
      {
        onSuccess: (playlist) => {
          add.mutate({ playlistId: playlist.id, clipId: clip.clipId });
          setMaking(false);
          setName("");
        },
      },
    );
  };

  return (
    <Dialog
      eyebrow={collectionsCopy.addTo.eyebrow}
      title={clip.video.title}
      onClose={onClose}
    >
      {() => (
        <div className="rs-modal-panel">
          {isLoading ? (
            <p className="rs-modal-note">{collectionsCopy.addTo.loading}</p>
          ) : collections.length === 0 ? (
            <p className="rs-modal-note">{collectionsCopy.addTo.none}</p>
          ) : (
            <ul className="rs-picker" aria-label={collectionsCopy.addTo.yours}>
              {collections.map((playlist, index) => {
                const known = details[index]?.data !== undefined;
                const has = holds(index) ?? false;
                return (
                  <li key={playlist.id}>
                    <button
                      type="button"
                      className="rs-picker-option"
                      aria-pressed={has}
                      disabled={!known || busy}
                      onClick={() => toggle(playlist.id, has)}
                    >
                      <span className="rs-picker-check" aria-hidden="true">
                        {has ? <IconCheck size={16} /> : null}
                      </span>
                      <span className="rs-picker-name">{playlist.name}</span>
                      <span className="rs-picker-meta">
                        {commonCopy.clips(playlist.clipCount)}
                      </span>
                    </button>
                  </li>
                );
              })}
            </ul>
          )}

          {making ? (
            <form className="rs-picker-new" onSubmit={makeWithClip}>
              <span className="rs-input-shell">
                <input
                  value={name}
                  onChange={(event) => setName(event.currentTarget.value)}
                  placeholder={collectionsCopy.addTo.newName}
                  aria-label={collectionsCopy.addTo.newName}
                  maxLength={100}
                  autoFocus
                />
              </span>
              <button
                className="rs-primary"
                type="submit"
                disabled={!name.trim() || busy}
              >
                {create.isPending
                  ? collectionsCopy.addTo.making
                  : collectionsCopy.addTo.makeAndAdd}
              </button>
            </form>
          ) : (
            <button
              type="button"
              className="rs-picker-make"
              onClick={() => setMaking(true)}
            >
              <IconPlus size={16} aria-hidden="true" />
              {collectionsCopy.addTo.newCollection}
            </button>
          )}

          {error ? (
            <p className="rs-modal-error" role="alert">
              {error instanceof Error
                ? error.message
                : collectionsCopy.changeFailed}
            </p>
          ) : null}
        </div>
      )}
    </Dialog>
  );
}
