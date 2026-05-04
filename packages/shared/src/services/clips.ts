import {
  type Clip,
  ClipsEndpointsApi,
  Configuration,
  type CreateClipResponse,
  type PagedClipsResponse,
  type TopTag,
} from "@repo/nucleus-api-client";
import { apiConfig } from "../config/apiConfig";

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
  params: FetchClipsParams
): Promise<PagedClipsResponse> {
  const { categoryId, page, pageSize, tags, titleSearch, unviewedOnly, sortOrder, startDate, endDate } = params;
  const api = new ClipsEndpointsApi(
    new Configuration({ basePath: apiConfig.baseUrl, credentials: "include" }),
  );

  // Convert comma-separated tags string to array if needed
  const tagsArray = tags ? tags.split(',').map(t => t.trim()) : undefined;

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
  const api = new ClipsEndpointsApi(
    new Configuration({ basePath: apiConfig.baseUrl, credentials: "include" }),
  );

  return api.createVideo({ categoryId, videoTitle: title, md5Hash, createdAt });
}

export async function getVideo(videoId: string): Promise<Clip | null> {
  const api = new ClipsEndpointsApi(
    new Configuration({ basePath: apiConfig.baseUrl, credentials: "include" }),
  );

  return api.getVideoById({ clipId: videoId });
}

export async function getTopTags(): Promise<Array<TopTag>> {
  const api = new ClipsEndpointsApi(
    new Configuration({ basePath: apiConfig.baseUrl, credentials: "include" }),
  );
  return api.getTopTags();
}

export async function addTagToVideo(
  clipId: string,
  tag: string,
): Promise<void> {
  const api = new ClipsEndpointsApi(
    new Configuration({ basePath: apiConfig.baseUrl, credentials: "include" }),
  );
  await api.addTagToClip({ clipId: clipId, addTagRequest: { tag: tag } });
}

export async function removeTagFromVideo(
  clipId: string,
  tag: string,
): Promise<void> {
  const api = new ClipsEndpointsApi(
    new Configuration({ basePath: apiConfig.baseUrl, credentials: "include" }),
  );
  await api.removeTagFromClip({ clipId: clipId, tag: tag });
}

export async function updateVideoTitle(clipId: string, title: string): Promise<void> {
  const api = new ClipsEndpointsApi(
    new Configuration({ basePath: apiConfig.baseUrl, credentials: "include" }),
  );
  await api.updateClipTitle({ clipId: clipId, updateTitleRequest: { title: title } });
}

export async function deleteVideo(clipId: string): Promise<void> {
  const api = new ClipsEndpointsApi(
    new Configuration({ basePath: apiConfig.baseUrl, credentials: "include" }),
  );
  await api.deleteClip({ clipId: clipId });
}

export async function markClipAsViewed(clipId: string): Promise<void> {
  const api = new ClipsEndpointsApi(
    new Configuration({ basePath: apiConfig.baseUrl, credentials: "include" }),
  );
  await api.markVideoAsViewed({ clipId: clipId });
}
