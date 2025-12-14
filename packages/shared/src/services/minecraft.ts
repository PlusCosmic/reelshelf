import {
  MinecraftEndpointsApi,
  Configuration,
  type ServerStatus,
  type OnlinePlayer,
  type DirectoryListing,
  type RconResponse,
} from "@repo/nucleus-api-client";
import { apiConfig } from "../config/apiConfig";

/**
 * Creates a configured instance of the MinecraftEndpointsApi
 * Uses credentials: "include" for cookie-based authentication
 */
function createApi() {
  return new MinecraftEndpointsApi(
    new Configuration({ basePath: apiConfig.baseUrl, credentials: "include" })
  );
}

// ============================================================================
// Server Status
// ============================================================================

/**
 * Fetches the current Minecraft server status
 * @returns Server status including online state, player count, version, etc.
 */
export async function getServerStatus(): Promise<ServerStatus> {
  const api = createApi();
  return api.getMinecraftStatus();
}

/**
 * Fetches the list of currently online players
 * @returns Array of online players with their names and UUIDs
 */
export async function getOnlinePlayers(): Promise<OnlinePlayer[]> {
  const api = createApi();
  return api.getMinecraftPlayers();
}

// ============================================================================
// Console / RCON
// ============================================================================

/**
 * Sends a command to the Minecraft server via RCON
 * @param command - The command to execute (without leading slash)
 * @returns The server's response to the command
 */
export async function sendCommand(command: string): Promise<RconResponse> {
  const api = createApi();
  return api.sendMinecraftCommand({ rconCommand: { command } });
}

/**
 * Gets the WebSocket URL for live console output
 * Converts the HTTP base URL to a WebSocket URL
 * @returns WebSocket URL for console streaming
 */
export function getConsoleWebSocketUrl(): string {
  return apiConfig.baseUrl.replace(/^http/, "ws") + "/minecraft/console/live";
}

// ============================================================================
// File Operations
// ============================================================================

/**
 * Lists the contents of a directory on the Minecraft server
 * @param path - The path to list (e.g., "/" for root, "/plugins" for plugins folder)
 * @returns Directory listing with files and subdirectories
 */
export async function listDirectory(path: string): Promise<DirectoryListing> {
  const api = createApi();
  return api.listMinecraftFiles({ path });
}

/**
 * Fetches the content of a file from the Minecraft server
 * @param path - The full path to the file
 * @returns The file content as a string
 *
 * Note: The API may return the content as either a raw string or wrapped
 * in an object like {content: "..."}. This function normalizes both formats.
 */
export async function getFileContent(path: string): Promise<string> {
  const api = createApi();
  const response = await api.getMinecraftFileContent({ path });

  // Handle case where API returns an object with content property
  // instead of a bare string (server implementation may vary)
  if (typeof response === "object" && response !== null && "content" in response) {
    return (response as { content: string }).content;
  }

  // If it's already a string, return as-is
  if (typeof response === "string") {
    return response;
  }

  // Fallback: stringify unexpected types to avoid silent failures
  return String(response ?? "");
}

/**
 * Saves content to a file on the Minecraft server
 * Creates the file if it doesn't exist, overwrites if it does
 * @param path - The full path to the file
 * @param content - The content to write to the file
 */
export async function saveFile(path: string, content: string): Promise<void> {
  const api = createApi();
  return api.saveMinecraftFileContent({ saveFileRequest: { path, content } });
}

/**
 * Deletes a file from the Minecraft server
 * @param path - The full path to the file to delete
 */
export async function deleteFile(path: string): Promise<void> {
  const api = createApi();
  return api.deleteMinecraftFile({ path });
}

/**
 * Creates a new directory on the Minecraft server
 * @param path - The full path for the new directory
 */
export async function createDirectory(path: string): Promise<void> {
  const api = createApi();
  return api.createMinecraftDirectory({ createDirectoryRequest: { path } });
}
