import {
  type Playlist,
  type PlaylistCollaborator,
  type UserProfile,
  type PlaylistSummary,
  type PlaylistWithDetails,
  type EnsureGamingSessionPlaylistRequest,
  type AddClipToPlaylistRequest,
} from "@/api-client";
import {
  createPlaylistApi as getPlaylistApi,
  createUserApi,
} from "./apiClients";

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

export async function createPlaylist(
  name: string,
  description: string | null,
): Promise<Playlist> {
  const api = getPlaylistApi();
  return api.createPlaylist({ createPlaylistRequest: { name, description } });
}

export async function removeClipFromPlaylist(
  playlistId: string,
  clipId: string,
): Promise<void> {
  const api = getPlaylistApi();
  await api.removeClipFromPlaylist({ id: playlistId, clipId });
}

export async function updatePlaylist(
  playlistId: string,
  name: string,
  description: string | null,
): Promise<Playlist> {
  const api = getPlaylistApi();
  return api.updatePlaylist({
    id: playlistId,
    updatePlaylistRequest: { name, description: description ?? "" },
  });
}

export async function deletePlaylist(playlistId: string): Promise<void> {
  const api = getPlaylistApi();
  await api.deletePlaylist({ id: playlistId });
}

/** Saves a collection's order as the full list of its clip ids, first to last. */
export async function reorderPlaylistClips(
  playlistId: string,
  clipOrdering: string[],
): Promise<PlaylistWithDetails> {
  const api = getPlaylistApi();
  return api.reorderPlaylistClips({
    id: playlistId,
    reorderPlaylistClipsRequest: { clipOrdering },
  });
}

export async function addCollaborator(
  playlistId: string,
  person: { userId: string } | { username: string },
): Promise<PlaylistCollaborator[]> {
  const api = getPlaylistApi();
  return api.addCollaborator({
    id: playlistId,
    addCollaboratorRequest: person,
  });
}

export async function removeCollaborator(
  playlistId: string,
  userId: string,
): Promise<void> {
  const api = getPlaylistApi();
  await api.removeCollaborator({ id: playlistId, userId });
}

/** People the viewer already shares a collection with; sign-up is open, so nobody else is offered. */
export async function fetchUserSuggestions(): Promise<UserProfile[]> {
  return createUserApi().getUserSuggestions();
}
