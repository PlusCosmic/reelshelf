export { apiConfig } from "./config/apiConfig";

export { logout } from "./services/auth";
export { fetchMe, fetchUser, fetchUserSuggestions } from "./services/user";
export { fetchCategories, fetchCategoryById, searchGames, addGameCategoryFromIgdb, addCustomCategory, removeCategory } from "./services/categories";
export { fetchClips, createVideoRequest, getVideo, addTagToVideo, removeTagFromVideo, getTopTags, updateVideoTitle, deleteVideo, markClipAsViewed, type FetchClipsParams } from "./services/clips";
export { downloadVideo } from "./services/ffmpeg";
export {
  fetchPlaylists,
  fetchPlaylistById,
  createPlaylist,
  createGamingSessionPlaylist,
  updatePlaylist,
  deletePlaylist,
  addClipsToPlaylist,
  removeClipFromPlaylist,
  reorderPlaylistClips,
  addCollaborator,
  removeCollaborator,
  fetchPlaylistCollaborators
} from "./services/playlists";
export { fetchUserPreferences, updateUserPreferences } from "./services/preferences";

export * from "./services/http";

export { useDebouncedValue } from "./hooks/useDebouncedValue";
