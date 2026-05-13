import {
  type Clip,
  type ClipLibraryResponse,
  type ClipShareResponse,
  type CreateClipResponse,
  type SharedClipResponse,
} from "@/api-client";
import { createClipsApi, createSharedClipsApi } from "./apiClients";

export async function fetchClipLibrary(): Promise<ClipLibraryResponse> {
  const api = createClipsApi();
  return api.getClipLibrary();
}

export async function createVideoRequest(
  categoryId: string,
  title: string,
  md5Hash?: string,
  createdAt?: Date,
): Promise<CreateClipResponse | null> {
  const api = createClipsApi();

  return api.createVideo({ categoryId, videoTitle: title, md5Hash, createdAt });
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
