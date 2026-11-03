import type {
  BinClipsFilter,
  BinnedClipsResponse,
  ClipBinResponse,
  DeleteBinnedClipsResponse,
} from "@/api-client";
import { createClipBinApi } from "./apiClients";

/** What goes in the bin: these clips, or every clip matching the library's filters. */
export type BinTarget =
  { clipIds: string[] } | { filter: Partial<BinClipsFilter> };

export async function binClips(
  target: BinTarget,
): Promise<BinnedClipsResponse> {
  const api = createClipBinApi();
  return api.binClips({
    binClipsRequest:
      "clipIds" in target
        ? { clipIds: target.clipIds, filter: null }
        : {
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

export async function restoreClips(
  clipIds: string[],
): Promise<BinnedClipsResponse> {
  const api = createClipBinApi();
  return api.restoreClips({ restoreClipsRequest: { clipIds } });
}

/** Deletes these binned clips for good, or with none the next part of the whole bin. */
export async function deleteBinnedClips(
  clipIds: string[] | null,
): Promise<DeleteBinnedClipsResponse> {
  const api = createClipBinApi();
  return api.deleteBinnedClips({ deleteBinnedClipsRequest: { clipIds } });
}

export async function fetchClipBin(
  page: number,
  pageSize: number,
): Promise<ClipBinResponse> {
  const api = createClipBinApi();
  return api.getClipBin({ page, pageSize });
}
