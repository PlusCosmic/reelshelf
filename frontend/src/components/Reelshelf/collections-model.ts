import type {
  Clip,
  PlaylistGameCount,
  PlaylistWithDetails,
} from "@/api-client";
import { bookBinding, type GameShelfItem } from "./reelshelf-model";

export type CollectionGame = {
  game: GameShelfItem | undefined;
  id: string;
  name: string;
  count: number;
  cloth: string;
};

/** A collection's clips in the order its people arranged them. */
export function collectionClips(playlist: PlaylistWithDetails | undefined) {
  return (playlist?.clips ?? [])
    .flatMap((item) =>
      item.clipDetails ? [{ item, clip: item.clipDetails }] : [],
    )
    .toSorted((a, b) => a.item.position - b.item.position);
}

/** Which games a collection's clips come from, most clips first, each in its cloth colour. */
export function collectionGames(
  clips: Clip[],
  shelf: GameShelfItem[],
): CollectionGame[] {
  const counts = new Map<string, number>();
  for (const clip of clips)
    counts.set(clip.gameCategoryId, (counts.get(clip.gameCategoryId) ?? 0) + 1);
  return gamesFromCounts(
    [...counts].map(([gameCategoryId, clipCount]) => ({
      gameCategoryId,
      clipCount,
    })),
    shelf,
  );
}

/** The same, from the per-game counts the collections list comes with. */
export function gamesFromCounts(
  counts: PlaylistGameCount[],
  shelf: GameShelfItem[],
): CollectionGame[] {
  return counts
    .map(({ gameCategoryId: id, clipCount: count }) => {
      const game = shelf.find((item) => item.id === id);
      return {
        game,
        id,
        name: game?.name ?? "Unknown game",
        count,
        cloth: game ? bookBinding(game).cloth : "var(--track-bg)",
      };
    })
    .toSorted((a, b) => b.count - a.count);
}

/** Total running time, rounded to whole minutes the way the rows show it. */
export function collectionMinutes(clips: Clip[]) {
  return minutesOf(clips.reduce((total, clip) => total + clip.video.length, 0));
}

export function minutesOf(seconds: number) {
  return Math.max(seconds > 0 ? 1 : 0, Math.round(seconds / 60));
}

/** Anyone on a collection, from its details or from the collections list. */
type Person = { userId: string; username: string; avatarUrl: string | null };

function names(people: Person[]) {
  const listed = people.flatMap((person) =>
    person.username ? [person.username] : [],
  );
  if (listed.length <= 2) return listed.join(", ");
  return `${listed[0]}, ${listed[1]} +${listed.length - 2}`;
}

/**
 * Who else is in on a collection, from the viewer's side: the people they share their own collection
 * with, or who shared someone else's collection with them.
 */
export function sharingSummary<P extends Person>(
  creatorUserId: string,
  everyone: P[],
  viewerId: string | undefined,
) {
  const others = everyone.filter((person) => person.userId !== viewerId);
  if (creatorUserId !== viewerId) {
    const owner = everyone.find((person) => person.userId === creatorUserId);
    return {
      people: owner ? [owner] : others.slice(0, 1),
      label: `Shared by ${owner?.username ?? "a friend"}`,
    };
  }
  if (others.length === 0) return { people: [], label: "Private" };
  return { people: others, label: `Shared with ${names(others)}` };
}
