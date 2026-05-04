import {
  type GameCategoryResponse,
  type GameSearchResult,
  Configuration,
  GameCategoryEndpointsApi,
} from "@repo/nucleus-api-client";
import { apiConfig } from "../config/apiConfig";

const getApi = () => new GameCategoryEndpointsApi(
  new Configuration({ basePath: apiConfig.baseUrl, credentials: "include" }),
);

export async function fetchCategories(): Promise<GameCategoryResponse[]> {
  return getApi().getUserGameCategories();
}

export async function fetchCategoryById(categoryId: string): Promise<GameCategoryResponse> {
  return getApi().getGameCategoryById({ categoryId });
}

export async function searchGames(query: string): Promise<GameSearchResult[]> {
  return getApi().searchGames({ query });
}

export async function addGameCategoryFromIgdb(igdbId: number): Promise<GameCategoryResponse> {
  return getApi().addGameCategoryFromIgdb({
    addGameFromIgdbRequest: { igdbId }
  });
}

export async function addCustomCategory(name: string, coverUrl?: string): Promise<GameCategoryResponse> {
  return getApi().addCustomCategory({
    addCustomCategoryRequest: { name, coverUrl: coverUrl ?? null }
  });
}

export async function removeCategory(categoryId: string): Promise<void> {
  await getApi().removeGameCategory({ categoryId });
}
