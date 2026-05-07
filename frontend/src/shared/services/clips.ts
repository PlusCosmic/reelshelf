import {
  type Clip,
  type CreateClipResponse,
  type PagedClipsResponse,
  type TopTag,
} from "@/api-client";
import { createClipsApi } from "./apiClients";

export interface FetchClipsParams {
  categoryId: string;
  page: number;
  pageSize: number;
  tags?: string;
  titleSearch?: string;
  unviewedOnly?: boolean;
  sortOrder?: number;
  startDate?: Date;
  endDate?: Date;
}

export async function fetchClips(
  params: FetchClipsParams,
): Promise<PagedClipsResponse> {
  const {
    categoryId,
    page,
    pageSize,
    tags,
    titleSearch,
    unviewedOnly,
    sortOrder,
    startDate,
    endDate,
  } = params;
  const api = createClipsApi();

  // Convert comma-separated tags string to array if needed
  const tagsArray = tags ? tags.split(",").map((t) => t.trim()) : undefined;

  return api.getVideosByCategory({
    categoryId,
    page,
    pageSize,
    tags: tagsArray,
    titleSearch,
    unviewedOnly,
    sortOrder,
    startDate,
    endDate,
  });
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

export async function getTopTags(): Promise<Array<TopTag>> {
  const api = createClipsApi();
  return api.getTopTags();
}

export async function addTagToVideo(
  clipId: string,
  tag: string,
): Promise<void> {
  const api = createClipsApi();
  await api.addTagToClip({ clipId: clipId, addTagRequest: { tag: tag } });
}

export async function removeTagFromVideo(
  clipId: string,
  tag: string,
): Promise<void> {
  const api = createClipsApi();
  await api.removeTagFromClip({ clipId: clipId, tag: tag });
}

export async function updateVideoTitle(
  clipId: string,
  title: string,
): Promise<void> {
  const api = createClipsApi();
  await api.updateClipTitle({
    clipId: clipId,
    updateTitleRequest: { title: title },
  });
}

export async function deleteVideo(clipId: string): Promise<void> {
  const api = createClipsApi();
  await api.deleteClip({ clipId: clipId });
}

export async function markClipAsViewed(clipId: string): Promise<void> {
  const api = createClipsApi();
  await api.markVideoAsViewed({ clipId: clipId });
}
