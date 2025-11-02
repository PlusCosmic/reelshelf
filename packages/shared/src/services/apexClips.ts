import {
  type Clip,
  ClipsEndpointsApi,
  Configuration,
  type CreateClipResponse,
  type PagedClipsResponse,
  type TopTag,
} from "@pluscosmic/nucleus-api-client";
import { apiConfig } from "../config/apiConfig";

export async function fetchApexClips(page: number, pageSize: number): Promise<PagedClipsResponse | null> {
  const api = new ClipsEndpointsApi(
    new Configuration({ basePath: apiConfig.baseUrl, credentials: "include" }),
  );
  return api.getVideosByCategory({ category: 0, page: page, pageSize: pageSize }).catch(() => null);
}

export async function createVideoRequest(
  title: string,
): Promise<CreateClipResponse | null> {
  const api = new ClipsEndpointsApi(
    new Configuration({ basePath: apiConfig.baseUrl, credentials: "include" }),
  );

  return api.createVideo({ category: 0, videoTitle: title });
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
