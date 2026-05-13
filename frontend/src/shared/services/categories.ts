import { type GameCategoryResponse, type GameSearchResult } from "@/api-client";
import { createGameCategoryApi } from "./apiClients";

const getApi = createGameCategoryApi;

export async function fetchCategories(): Promise<GameCategoryResponse[]> {
  return getApi().getUserGameCategories();
}

export async function searchGames(query: string): Promise<GameSearchResult[]> {
  return getApi().searchGames({ query });
}

export async function addGameCategoryFromIgdb(
  igdbId: number,
): Promise<GameCategoryResponse> {
  return getApi().addGameCategoryFromIgdb({
    addGameFromIgdbRequest: { igdbId },
  });
}

export async function addCustomCategory(
  name: string,
  coverUrl?: string,
): Promise<GameCategoryResponse> {
  return getApi().addCustomCategory({
    addCustomCategoryRequest: { name, coverUrl: coverUrl ?? null },
  });
}
