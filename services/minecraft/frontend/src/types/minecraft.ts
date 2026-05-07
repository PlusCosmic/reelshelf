// Re-export types from the generated API client for consistency
// These match the backend models exactly

export type {
  ServerStatus,
  OnlinePlayer,
  FileEntry,
  DirectoryListing,
  RconCommand,
  RconResponse,
  CommandLogEntry,
  SaveFileRequest,
  CreateDirectoryRequest,
} from '@repo/minecraft-api-client';

// Additional frontend-only types for WebSocket messages
export interface WsLogEntry {
  type: 'log';
  text: string;
  level: 'information' | 'warning' | 'error' | 'debug';
  timestamp: string;
}

export interface WsCommandResponse {
  type: 'response';
  command: string;
  response: string | null;
  error: string | null;
  success: boolean;
  timestamp: string;
}

export interface WsConnectedMessage {
  type: 'connected';
  message: string;
}

export interface WsErrorMessage {
  type: 'error';
  error: string;
}

export type WsMessage = WsLogEntry | WsCommandResponse | WsConnectedMessage | WsErrorMessage;

// Console entry type for the terminal display
export interface ConsoleEntry {
  id: string;
  type: 'log' | 'command' | 'response' | 'error' | 'system';
  text: string;
  level?: string;
  timestamp: Date;
  command?: string;
  success?: boolean;
}
