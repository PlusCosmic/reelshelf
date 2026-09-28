import { useNavigate } from "@tanstack/react-router";
import type { FormEvent } from "react";
import { useState } from "react";
import { IconUserPlus } from "@tabler/icons-react";
import type { PlaylistWithDetails } from "@/api-client";
import {
  useInviteToCollection,
  useRemovePersonFromCollection,
  useUserSuggestions,
} from "@/hooks/playlists.queries";
import { Dialog } from "../Dialog";
import { Avatar } from "../ReelshelfPrimitives";

/**
 * Everyone on a collection. Its owner invites people, from those they already share collections with
 * or by username, and removes them; anyone else can leave. See PlaylistPermissions in the API.
 */
export function PeopleDialog({
  playlist,
  viewerId,
  onClose,
}: {
  playlist: PlaylistWithDetails;
  viewerId: string | undefined;
  onClose: () => void;
}) {
  const navigate = useNavigate();
  const isOwner = playlist.creatorUserId === viewerId;
  const invite = useInviteToCollection(playlist.id);
  const remove = useRemovePersonFromCollection(playlist.id);
  const { data: suggestions = [] } = useUserSuggestions(isOwner);
  const [username, setUsername] = useState("");
  const busy = invite.isPending || remove.isPending;
  const error = invite.error ?? remove.error;

  const members = new Set(
    playlist.collaborators.map((person) => person.userId),
  );
  const people = playlist.collaborators.toSorted((a, b) =>
    a.userId === playlist.creatorUserId
      ? -1
      : b.userId === playlist.creatorUserId
        ? 1
        : a.username.localeCompare(b.username),
  );
  const offered = suggestions.filter((person) => !members.has(person.id));

  const inviteByName = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const name = username.trim().replace(/^@/, "");
    if (!name) return;
    invite.mutate({ username: name }, { onSuccess: () => setUsername("") });
  };

  const leave = () => {
    if (!viewerId) return;
    if (!window.confirm(`Leave "${playlist.name}"? You'll need inviting back.`))
      return;
    remove.mutate(viewerId, {
      onSuccess: () => void navigate({ to: "/collections" }),
    });
  };

  return (
    <Dialog
      eyebrow={playlist.name}
      title="People"
      busy={busy}
      onClose={onClose}
    >
      {() => (
        <div className="rs-modal-panel">
          <ul className="rs-people" aria-label="On this collection">
            {people.map((person) => {
              const owner = person.userId === playlist.creatorUserId;
              const you = person.userId === viewerId;
              return (
                <li key={person.userId}>
                  <Avatar
                    name={person.username}
                    src={person.avatarUrl}
                    size={32}
                  />
                  <span className="rs-people-name">
                    {person.username}
                    {you ? <span className="rs-people-tag">You</span> : null}
                  </span>
                  <span className="rs-people-role">
                    {owner ? "Made it" : "Can add clips"}
                  </span>
                  {isOwner && !owner ? (
                    <button
                      type="button"
                      className="rs-small-button"
                      disabled={busy}
                      onClick={() => remove.mutate(person.userId)}
                    >
                      Remove
                    </button>
                  ) : !isOwner && you ? (
                    <button
                      type="button"
                      className="rs-small-button"
                      disabled={busy}
                      onClick={leave}
                    >
                      Leave
                    </button>
                  ) : (
                    <span />
                  )}
                </li>
              );
            })}
          </ul>

          {isOwner ? (
            <section className="rs-people-invite" aria-labelledby="rs-invite">
              <h3 id="rs-invite" className="rs-eyebrow">
                Invite
              </h3>
              {offered.length > 0 ? (
                <div className="rs-people-suggestions">
                  {offered.map((person) => (
                    <button
                      key={person.id}
                      type="button"
                      className="rs-people-suggestion"
                      disabled={busy}
                      onClick={() => invite.mutate({ userId: person.id })}
                    >
                      <Avatar
                        name={person.username}
                        src={person.avatar}
                        size={22}
                      />
                      {person.globalName || person.username}
                      <IconUserPlus size={15} aria-hidden="true" />
                    </button>
                  ))}
                </div>
              ) : null}
              <form className="rs-picker-new" onSubmit={inviteByName}>
                <span className="rs-input-shell">
                  <input
                    value={username}
                    onChange={(event) => setUsername(event.currentTarget.value)}
                    placeholder="Their Discord or Twitch username"
                    aria-label="Username to invite"
                  />
                </span>
                <button
                  className="rs-primary"
                  type="submit"
                  disabled={!username.trim() || busy}
                >
                  {invite.isPending ? "Inviting…" : "Invite"}
                </button>
              </form>
              <p className="rs-people-note">
                People you invite can watch everything in it and add their own
                clips.
              </p>
            </section>
          ) : null}

          {error ? (
            <p className="rs-modal-error" role="alert">
              {error instanceof Error
                ? error.message
                : "That change could not be saved."}
            </p>
          ) : null}
        </div>
      )}
    </Dialog>
  );
}
