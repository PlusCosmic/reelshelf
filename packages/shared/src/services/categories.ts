import {
  type ClipCategory,
  Configuration,
  ClipsEndpointsApi,
} from "@pluscosmic/nucleus-api-client";
import { apiConfig } from "../config/apiConfig";

export async function fetchCategories(): Promise<ClipCategory[] | null> {
  const api = new ClipsEndpointsApi(
    new Configuration({ basePath: apiConfig.baseUrl, credentials: "include" }),
  );
  return api.getCategories().catch(() => null);
}
