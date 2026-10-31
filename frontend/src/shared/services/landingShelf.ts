import type { LandingShelfGame } from "@/api-client";
import { createLandingShelfApi } from "./apiClients";

/** Today's popular games for the landing page's example shelf; empty until the API has fetched them. */
export async function fetchLandingShelf(): Promise<LandingShelfGame[]> {
  const shelf = await createLandingShelfApi().getLandingShelf();
  return shelf.games;
}
