import {
  type Clip,
  Configuration,
  ClipsEndpointsApi,
} from "@pluscosmic/nucleus-api-client";
import { apiConfig } from "../config/apiConfig";

export async function fetchApexClips(page: number): Promise<Clip[] | null> {
  const api = new ClipsEndpointsApi(
    new Configuration({ basePath: apiConfig.baseUrl, credentials: "include" }),
  );
  return api.getVideosByCategory({ category: 1, page: page }).catch(() => null);
}
