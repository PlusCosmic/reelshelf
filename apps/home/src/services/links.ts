import { apiConfig } from "@repo/shared";
import {
  Configuration,
  type LinkRequest,
  LinksEndpointsApi,
  type UserFrequentLinkRow,
} from "@repo/nucleus-api-client";

export interface NewLinkInput {
  url: string;
}

export async function getLinks(): Promise<UserFrequentLinkRow[]> {
  const api = new LinksEndpointsApi(
    new Configuration({ basePath: apiConfig.baseUrl, credentials: "include" }),
  );
  return await api.getLinksForUser();
}

export async function addLink(input: NewLinkInput): Promise<void> {
  const api = new LinksEndpointsApi(
    new Configuration({ basePath: apiConfig.baseUrl, credentials: "include" }),
  );
  const linkRequest: LinkRequest = { url: input.url };
  await api.addLink({ linkRequest });
}

export async function deleteLink(id: string): Promise<void> {
  const api = new LinksEndpointsApi(
    new Configuration({ basePath: apiConfig.baseUrl, credentials: "include" }),
  );
  await api.deleteLink({ id });
}
