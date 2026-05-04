import {
  type PlaylistSummary,
  type PlaylistWithDetails,
  type CreatePlaylistRequest,
  type CreateGamingSessionPlaylistRequest,
  type UpdatePlaylistRequest,
  type AddClipToPlaylistRequest,
  type AddCollaboratorRequest,
  type ReorderPlaylistClipsRequest,
  PlaylistEndpointsApi,
  Configuration,
} from "@repo/nucleus-api-client";
import { apiConfig } from "../config/apiConfig";

// Helper to create API instance with auth
function getPlaylistApi() {
  return new PlaylistEndpointsApi(
    new Configuration({ basePath: apiConfig.baseUrl, credentials: "include" }),
  );
}

/**
 * Fetch all playlists the user has access to (created or collaborating on)
 */
export async function fetchPlaylists(): Promise<PlaylistSummary[]> {
  const api = getPlaylistApi();
  return api.getPlaylists();
}

/**
 * Fetch detailed playlist information including clips and collaborators
 */
export async function fetchPlaylistById(playlistId: string): Promise<PlaylistWithDetails> {
  const api = getPlaylistApi();
  return api.getPlaylistById({ id: playlistId });
}

/**
 * Create a new playlist
 */
export async function createPlaylist(request: CreatePlaylistRequest): Promise<any> {
  const api = getPlaylistApi();
  return api.createPlaylist({ createPlaylistRequest: request });
}

/**
 * Create a gaming session playlist from the last 24 hours of clips
 * Automatically compiles clips from selected participants for the chosen game
 */
export async function createGamingSessionPlaylist(
  request: CreateGamingSessionPlaylistRequest
): Promise<PlaylistWithDetails> {
  const api = getPlaylistApi();
  return api.createGamingSessionPlaylist({
    createGamingSessionPlaylistRequest: request,
  });
}

/**
 * Update playlist details (name, description)
 */
export async function updatePlaylist(
  playlistId: string,
  request: UpdatePlaylistRequest
): Promise<any> {
  const api = getPlaylistApi();
  return api.updatePlaylist({
    id: playlistId,
    updatePlaylistRequest: request,
  });
}

/**
 * Delete a playlist
 */
export async function deletePlaylist(playlistId: string): Promise<void> {
  const api = getPlaylistApi();
  return api.deletePlaylist({ id: playlistId });
}

/**
 * Add clip(s) to a playlist
 */
export async function addClipsToPlaylist(
  playlistId: string,
  request: AddClipToPlaylistRequest
): Promise<any> {
  const api = getPlaylistApi();
  return api.addClipsToPlaylist({
    id: playlistId,
    addClipToPlaylistRequest: request,
  });
}

/**
 * Remove a clip from a playlist
 */
export async function removeClipFromPlaylist(
  playlistId: string,
  clipId: string
): Promise<void> {
  const api = getPlaylistApi();
  return api.removeClipFromPlaylist({
    id: playlistId,
    clipId,
  });
}

/**
 * Reorder clips in a playlist
 */
export async function reorderPlaylistClips(
  playlistId: string,
  request: ReorderPlaylistClipsRequest
): Promise<any> {
  const api = getPlaylistApi();
  return api.reorderPlaylistClips({
    id: playlistId,
    reorderPlaylistClipsRequest: request,
  });
}

/**
 * Add a collaborator to a playlist (sends Discord DM if enabled)
 */
export async function addCollaborator(
  playlistId: string,
  request: AddCollaboratorRequest
): Promise<any[]> {
  const api = getPlaylistApi();
  return api.addCollaborator({
    id: playlistId,
    addCollaboratorRequest: request,
  });
}

/**
 * Remove a collaborator from a playlist
 */
export async function removeCollaborator(
  playlistId: string,
  userId: string
): Promise<void> {
  const api = getPlaylistApi();
  return api.removeCollaborator({
    id: playlistId,
    userId,
  });
}

/**
 * Get all collaborators for a playlist
 */
export async function fetchPlaylistCollaborators(
  playlistId: string
): Promise<any[]> {
  const api = getPlaylistApi();
  return api.getCollaborators({ id: playlistId });
}
