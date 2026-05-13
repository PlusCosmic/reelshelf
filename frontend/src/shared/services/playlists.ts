import {
  type PlaylistSummary,
  type PlaylistWithDetails,
  type EnsureGamingSessionPlaylistRequest,
  type AddClipToPlaylistRequest,
} from "@/api-client";
import { createPlaylistApi as getPlaylistApi } from "./apiClients";

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
export async function fetchPlaylistById(
  playlistId: string,
): Promise<PlaylistWithDetails> {
  const api = getPlaylistApi();
  return api.getPlaylistById({ id: playlistId });
}

export async function ensureGamingSessionPlaylist(
  request: EnsureGamingSessionPlaylistRequest,
): Promise<PlaylistWithDetails> {
  const api = getPlaylistApi();
  return api.ensureGamingSessionPlaylist({
    ensureGamingSessionPlaylistRequest: request,
  });
}

/**
 * Add clip(s) to a playlist
 */
export async function addClipsToPlaylist(
  playlistId: string,
  request: AddClipToPlaylistRequest,
): Promise<PlaylistWithDetails> {
  const api = getPlaylistApi();
  return api.addClipsToPlaylist({
    id: playlistId,
    addClipToPlaylistRequest: request,
  });
}
