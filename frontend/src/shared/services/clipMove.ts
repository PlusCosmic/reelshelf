import type { MoveClipsResponse } from "@/api-client";
import { createClipsApi } from "./apiClients";
import type { BinTarget } from "./bin";

/** Which clips move: these clips, or every clip matching the library's filters, as the bin takes them. */
export type MoveTarget = BinTarget;

/** Moves clips to another book; answers the ones moved, each with the book it came from, and the ones left. */
export async function moveClips(
  target: MoveTarget,
  categoryId: string,
): Promise<MoveClipsResponse> {
  const api = createClipsApi();
  return api.moveClips({
    moveClipsRequest:
      "clipIds" in target
        ? { categoryId, clipIds: target.clipIds, filter: null }
        : {
            categoryId,
            clipIds: null,
            filter: {
              categoryId: target.filter.categoryId ?? null,
              tags: target.filter.tags ?? null,
              search: target.filter.search ?? null,
              legend: target.filter.legend ?? null,
              unviewedOnly: target.filter.unviewedOnly ?? false,
            },
          },
  });
}
