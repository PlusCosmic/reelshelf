import {
  type Clip,
  type ClipLibraryResponse,
  type GetClipsRequest,
  type LegendCount,
  type PagedClipsResponse,
  type TopTag,
  type ClipShareResponse,
  type CreateClipResponse,
  type SharedClipResponse,
} from "@/api-client";
import { createClipsApi, createSharedClipsApi } from "./apiClients";

export async function fetchClipLibrary(): Promise<ClipLibraryResponse> {
  const api = createClipsApi();
  return api.getClipLibrary();
}

export async function fetchClips(
  request: GetClipsRequest,
): Promise<PagedClipsResponse> {
  const api = createClipsApi();
  return api.getClips(request);
}

export async function fetchTopTags(categoryId?: string): Promise<TopTag[]> {
  const api = createClipsApi();
  return api.getTopTags({ categoryId });
}

export async function fetchClipLegends(): Promise<LegendCount[]> {
  const api = createClipsApi();
  return api.getClipLegends();
}

export async function createVideoRequest(
  categoryId: string,
  title: string,
  fileSize: number,
  md5Hash?: string,
  createdAt?: Date,
): Promise<CreateClipResponse | null> {
  const api = createClipsApi();

  return api.createVideo({
    categoryId,
    videoTitle: title,
    fileSize,
    md5Hash,
    createdAt,
  });
}

/** The hashes, of files about to go into the book, that the owner already has a clip of there. */
export async function findDuplicateVideos(
  categoryId: string,
  md5Hashes: string[],
): Promise<string[]> {
  const api = createClipsApi();
  const response = await api.findDuplicateVideos({
    categoryId,
    findDuplicateVideosRequest: { md5Hashes },
  });
  return response.duplicates;
}

export async function getVideo(videoId: string): Promise<Clip | null> {
  const api = createClipsApi();

  return api.getVideoById({ clipId: videoId });
}

export async function addTagToVideo(
  clipId: string,
  tag: string,
): Promise<void> {
  const api = createClipsApi();
  await api.addTagToClip({ clipId: clipId, addTagRequest: { tag: tag } });
}

export async function deleteClip(clipId: string): Promise<void> {
  const api = createClipsApi();
  await api.deleteClip({ clipId });
}

export async function markClipAsViewed(clipId: string): Promise<void> {
  const api = createClipsApi();
  await api.markVideoAsViewed({ clipId: clipId });
}

export async function shareVideo(clipId: string): Promise<ClipShareResponse> {
  const api = createClipsApi();
  return api.shareVideo({ clipId });
}

export async function getSharedClip(
  token: string,
): Promise<SharedClipResponse> {
  const api = createSharedClipsApi();
  return api.getSharedClip({ token });
}
