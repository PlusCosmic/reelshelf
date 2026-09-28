import { Link } from "@tanstack/react-router";
import type { KeyboardEvent, PointerEvent } from "react";
import { useEffect, useRef, useState } from "react";
import { IconGripVertical, IconX } from "@tabler/icons-react";
import type {
  Clip,
  GameCategoryResponse,
  PlaylistClip,
  PlaylistWithDetails,
} from "@/api-client";
import { Avatar, ClipThumb } from "../ReelshelfPrimitives";
import { categoryForClip, formatDuration } from "../reelshelf-model";
import { StitchedDot } from "../StitchedDot";

type Entry = { item: PlaylistClip; clip: Clip };

function move(order: string[], id: string, to: number) {
  const next = order.filter((item) => item !== id);
  next.splice(to, 0, id);
  return next;
}

/**
 * A collection's clips in its order. Everyone on the collection can drag a clip by its handle, or
 * move it with the arrow keys, and take clips out. Positions aren't numbered: the order shows in the
 * list itself.
 */
export function CollectionRows({
  playlist,
  entries,
  categories,
  showGame,
  viewerId,
  onReorder,
  onRemove,
  removingId,
}: {
  playlist: PlaylistWithDetails;
  entries: Entry[];
  categories: GameCategoryResponse[];
  showGame: boolean;
  viewerId: string | undefined;
  onReorder: (clipIds: string[]) => void;
  onRemove: (clip: Clip) => void;
  removingId: string | null;
}) {
  const listRef = useRef<HTMLOListElement>(null);
  const [dragOrder, setDragOrder] = useState<string[] | null>(null);
  const [draggingId, setDraggingId] = useState<string | null>(null);
  const [announcement, setAnnouncement] = useState("");
  const order = entries.map(({ clip }) => clip.clipId);
  const shown = dragOrder ?? order;
  const byId = new Map(entries.map((entry) => [entry.clip.clipId, entry]));

  const people = new Map(
    playlist.collaborators.map((person) => [person.userId, person]),
  );
  const addedBy = (userId: string) => {
    if (userId === viewerId) return { name: "you", person: people.get(userId) };
    const person = people.get(userId);
    return person ? { name: person.username, person } : null;
  };

  const announce = (id: string, next: string[]) => {
    const title = byId.get(id)?.clip.video.title ?? "Clip";
    setAnnouncement(
      `${title} moved to ${next.indexOf(id) + 1} of ${next.length}`,
    );
  };

  const startDrag = (event: PointerEvent<HTMLButtonElement>, id: string) => {
    if (event.button !== 0) return;
    event.preventDefault();
    setDraggingId(id);
    setDragOrder(order);
  };

  // Follow the pointer on the window rather than capturing it on the handle: moving the row in the
  // list moves its element in the page, which would drop a capture partway through the drag.
  const latest = useRef({ dragOrder, order, onReorder, announce });
  latest.current = { dragOrder, order, onReorder, announce };
  useEffect(() => {
    if (!draggingId) return;
    const follow = (event: globalThis.PointerEvent) => {
      const current = latest.current.dragOrder;
      if (!current || !listRef.current) return;
      const others = [
        ...listRef.current.querySelectorAll<HTMLElement>("[data-clip-id]"),
      ].filter((row) => row.dataset.clipId !== draggingId);
      let to = others.findIndex((row) => {
        const box = row.getBoundingClientRect();
        return event.clientY < box.top + box.height / 2;
      });
      if (to === -1) to = others.length;
      const next = move(current, draggingId, to);
      if (next.join() !== current.join()) setDragOrder(next);
    };
    const drop = () => {
      const { dragOrder: final, order: before } = latest.current;
      if (final && final.join() !== before.join()) {
        latest.current.onReorder(final);
        latest.current.announce(draggingId, final);
      }
      setDraggingId(null);
      setDragOrder(null);
    };
    window.addEventListener("pointermove", follow);
    window.addEventListener("pointerup", drop);
    window.addEventListener("pointercancel", drop);
    return () => {
      window.removeEventListener("pointermove", follow);
      window.removeEventListener("pointerup", drop);
      window.removeEventListener("pointercancel", drop);
    };
  }, [draggingId]);

  const nudge = (event: KeyboardEvent<HTMLButtonElement>, id: string) => {
    const step =
      event.key === "ArrowUp" ? -1 : event.key === "ArrowDown" ? 1 : 0;
    if (!step) return;
    event.preventDefault();
    const to = order.indexOf(id) + step;
    if (to < 0 || to >= order.length) return;
    const next = move(order, id, to);
    onReorder(next);
    announce(id, next);
  };

  return (
    <>
      <p id="rs-reorder-help" className="rs-visually-hidden">
        Drag, or press the up and down arrow keys, to move a clip.
      </p>
      <p className="rs-visually-hidden" aria-live="polite">
        {announcement}
      </p>
      <ol className="rs-collection-rows" ref={listRef}>
        {shown.map((id) => {
          const entry = byId.get(id);
          if (!entry) return null;
          const { item, clip } = entry;
          const category = categoryForClip(clip, categories);
          const adder = addedBy(item.addedByUserId);
          return (
            <li
              key={id}
              data-clip-id={id}
              className={`rs-collection-item${id === draggingId ? " dragging" : ""}`}
            >
              <button
                type="button"
                className="rs-drag-handle"
                aria-label={`Move ${clip.video.title}`}
                aria-describedby="rs-reorder-help"
                onPointerDown={(event) => startDrag(event, id)}
                onKeyDown={(event) => nudge(event, id)}
              >
                <IconGripVertical size={18} aria-hidden="true" />
              </button>
              <Link
                className={`rs-row${showGame ? "" : " no-game"}`}
                to="/games/$slug/$clipId"
                params={{ slug: clip.categorySlug, clipId: clip.clipId }}
                draggable={false}
              >
                <ClipThumb clip={clip} category={category} compact />
                <span className="rs-row-body">
                  <span className="rs-row-heading">
                    <strong className="rs-row-title">{clip.video.title}</strong>
                    {!clip.isViewed ? (
                      <>
                        <StitchedDot className="rs-row-new" />
                        <span className="rs-visually-hidden">
                          Not watched yet
                        </span>
                      </>
                    ) : null}
                  </span>
                  {adder ? (
                    <span className="rs-collection-added">
                      <Avatar
                        name={adder.person?.username}
                        src={adder.person?.avatarUrl}
                        size={18}
                      />
                      Added by {adder.name}
                    </span>
                  ) : null}
                </span>
                {showGame ? (
                  <span className="wide-only rs-row-game">
                    {category?.coverUrl ? (
                      <img src={category.coverUrl} alt="" loading="lazy" />
                    ) : null}
                    {category?.name ?? "Game"}
                  </span>
                ) : null}
                <span className="wide-only rs-meta rs-row-duration">
                  {formatDuration(clip.video.length)}
                </span>
              </Link>
              <button
                type="button"
                className="rs-icon-button rs-collection-remove"
                aria-label={`Take ${clip.video.title} out of this collection`}
                title="Take out of this collection"
                disabled={removingId === id}
                onClick={() => onRemove(clip)}
              >
                <IconX size={16} aria-hidden="true" />
              </button>
            </li>
          );
        })}
      </ol>
    </>
  );
}
