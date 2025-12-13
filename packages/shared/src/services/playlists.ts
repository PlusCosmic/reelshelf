import {
  type PlaylistSummary,
  type PlaylistWithDetails,
  type CreatePlaylistRequest,
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
  return api.getPlaylists().catch((error: any) => {
    console.error('Failed to fetch playlists:', error);
    return [];
  });
}

/**
 * Fetch detailed playlist information including clips and collaborators
 */
export async function fetchPlaylistById(playlistId: string): Promise<PlaylistWithDetails | null> {
  const api = getPlaylistApi();
  return api.getPlaylistById({ id: playlistId }).catch((error: any) => {
    console.error('Failed to fetch playlist:', { playlistId, error });
    return null;
  });
}

/**
 * Create a new playlist
 */
export async function createPlaylist(
  request: CreatePlaylistRequest
): Promise<any | null> {
  const api = getPlaylistApi();
  return api.createPlaylist({ createPlaylistRequest: request }).catch((error: any) => {
    console.error('Failed to create playlist:', { request, error });
    throw error;
  });
}

/**
 * Update playlist details (name, description)
 */
export async function updatePlaylist(
  playlistId: string,
  request: UpdatePlaylistRequest
): Promise<any | null> {
  const api = getPlaylistApi();
  return api.updatePlaylist({
    id: playlistId,
    updatePlaylistRequest: request,
  }).catch((error: any) => {
    console.error('Failed to update playlist:', { playlistId, request, error });
    throw error;
  });
}

/**
 * Delete a playlist
 */
export async function deletePlaylist(playlistId: string): Promise<void> {
  const api = getPlaylistApi();
  return api.deletePlaylist({ id: playlistId }).catch((error: any) => {
    console.error('Failed to delete playlist:', { playlistId, error });
    throw error;
  });
}

/**
 * Add clip(s) to a playlist
 */
export async function addClipsToPlaylist(
  playlistId: string,
  request: AddClipToPlaylistRequest
): Promise<any | null> {
  const api = getPlaylistApi();
  return api.addClipsToPlaylist({
    id: playlistId,
    addClipToPlaylistRequest: request,
  }).catch((error: any) => {
    console.error('Failed to add clips to playlist:', { playlistId, request, error });
    throw error;
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
  }).catch((error: any) => {
    console.error('Failed to remove clip from playlist:', { playlistId, clipId, error });
    throw error;
  });
}

/**
 * Reorder clips in a playlist
 */
export async function reorderPlaylistClips(
  playlistId: string,
  request: ReorderPlaylistClipsRequest
): Promise<any | null> {
  const api = getPlaylistApi();
  return api.reorderPlaylistClips({
    id: playlistId,
    reorderPlaylistClipsRequest: request,
  }).catch((error: any) => {
    console.error('Failed to reorder playlist clips:', { playlistId, request, error });
    throw error;
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
  }).catch((error: any) => {
    console.error('Failed to add collaborator:', { playlistId, request, error });
    throw error;
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
  }).catch((error: any) => {
    console.error('Failed to remove collaborator:', { playlistId, userId, error });
    throw error;
  });
}

/**
 * Get all collaborators for a playlist
 */
export async function fetchPlaylistCollaborators(
  playlistId: string
): Promise<any[]> {
  const api = getPlaylistApi();
  return api.getCollaborators({ id: playlistId }).catch((error: any) => {
    console.error('Failed to fetch collaborators:', { playlistId, error });
    return [];
  });
}
