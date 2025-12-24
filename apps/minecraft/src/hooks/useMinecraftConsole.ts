import { useEffect, useState, useRef, useCallback } from 'react';
import { getConsoleWebSocketUrl } from '@repo/shared/services/minecraft';

export interface LogEntry {
  type: 'log';
  text: string;
  level: 'information' | 'warning' | 'error' | 'debug';
  timestamp: string;
}

export interface CommandResponse {
  type: 'response';
  command: string;
  response: string | null;
  error: string | null;
  success: boolean;
  timestamp: string;
}

export interface ConnectedMessage {
  type: 'connected';
  message: string;
}

export interface ErrorMessage {
  type: 'error';
  error: string;
}

export type WsMessage = LogEntry | CommandResponse | ConnectedMessage | ErrorMessage;

export interface ConsoleEntry {
  id: string;
  type: 'log' | 'command' | 'response' | 'error' | 'system';
  text: string;
  level?: string;
  timestamp: Date;
  command?: string;
  success?: boolean;
}

interface UseMinecraftConsoleOptions {
  serverId: string | undefined;
  enabled?: boolean;
  maxEntries?: number;
}

export function useMinecraftConsole(options: UseMinecraftConsoleOptions) {
  const { serverId, enabled = true, maxEntries = 1000 } = options;

  const [isConnected, setIsConnected] = useState(false);
  const [isConnecting, setIsConnecting] = useState(false);
  const [entries, setEntries] = useState<ConsoleEntry[]>([]);
  const [connectionError, setConnectionError] = useState<string | null>(null);

  const wsRef = useRef<WebSocket | null>(null);
  const reconnectTimeoutRef = useRef<number | null>(null);
  const reconnectAttempts = useRef(0);
  const entryIdCounter = useRef(0);

  const addEntry = useCallback((entry: Omit<ConsoleEntry, 'id'>) => {
    const newEntry: ConsoleEntry = {
      ...entry,
      id: `entry-${entryIdCounter.current++}`,
    };

    setEntries(prev => {
      const updated = [...prev, newEntry];
      // Keep only the last maxEntries
      if (updated.length > maxEntries) {
        return updated.slice(-maxEntries);
      }
      return updated;
    });
  }, [maxEntries]);

  const handleMessage = useCallback((event: MessageEvent) => {
    try {
      const data: WsMessage = JSON.parse(event.data);

      switch (data.type) {
        case 'log':
          addEntry({
            type: 'log',
            text: data.text,
            level: data.level,
            timestamp: new Date(data.timestamp),
          });
          break;

        case 'response':
          addEntry({
            type: data.success ? 'response' : 'error',
            text: data.response || data.error || '',
            command: data.command,
            success: data.success,
            timestamp: new Date(data.timestamp),
          });
          break;

        case 'connected':
          addEntry({
            type: 'system',
            text: data.message,
            timestamp: new Date(),
          });
          break;

        case 'error':
          addEntry({
            type: 'error',
            text: data.error,
            timestamp: new Date(),
          });
          break;
      }
    } catch (error) {
      console.error('Failed to parse WebSocket message:', error);
    }
  }, [addEntry]);

  const connect = useCallback(() => {
    if (!serverId) {
      setConnectionError('No server selected');
      return;
    }

    if (wsRef.current?.readyState === WebSocket.OPEN) {
      return;
    }

    setIsConnecting(true);
    setConnectionError(null);

    const wsUrl = getConsoleWebSocketUrl(serverId);

    try {
      const ws = new WebSocket(wsUrl);
      wsRef.current = ws;

      ws.onopen = () => {
        setIsConnected(true);
        setIsConnecting(false);
        setConnectionError(null);
        reconnectAttempts.current = 0;
      };

      ws.onmessage = handleMessage;

      ws.onerror = (error) => {
        console.error('WebSocket error:', error);
        setConnectionError('Connection error');
      };

      ws.onclose = (event) => {
        setIsConnected(false);
        setIsConnecting(false);
        wsRef.current = null;

        if (!event.wasClean && enabled && serverId) {
          // Attempt to reconnect with exponential backoff
          const delay = Math.min(1000 * Math.pow(2, reconnectAttempts.current), 30000);
          reconnectAttempts.current++;

          addEntry({
            type: 'system',
            text: `Disconnected. Reconnecting in ${delay / 1000}s...`,
            timestamp: new Date(),
          });

          reconnectTimeoutRef.current = window.setTimeout(() => {
            connect();
          }, delay);
        }
      };
    } catch (error) {
      setIsConnecting(false);
      setConnectionError('Failed to create WebSocket connection');
      console.error('Failed to create WebSocket:', error);
    }
  }, [enabled, serverId, handleMessage, addEntry]);

  const disconnect = useCallback(() => {
    if (reconnectTimeoutRef.current) {
      clearTimeout(reconnectTimeoutRef.current);
      reconnectTimeoutRef.current = null;
    }

    if (wsRef.current) {
      wsRef.current.close();
      wsRef.current = null;
    }

    setIsConnected(false);
    setIsConnecting(false);
  }, []);

  const sendCommand = useCallback((command: string) => {
    if (!wsRef.current || wsRef.current.readyState !== WebSocket.OPEN) {
      addEntry({
        type: 'error',
        text: 'Not connected to server',
        timestamp: new Date(),
      });
      return false;
    }

    // Add command entry immediately for visual feedback
    addEntry({
      type: 'command',
      text: command,
      timestamp: new Date(),
    });

    wsRef.current.send(JSON.stringify({
      type: 'command',
      command,
    }));

    return true;
  }, [addEntry]);

  const clearEntries = useCallback(() => {
    setEntries([]);
  }, []);

  // Connect on mount, disconnect on unmount
  useEffect(() => {
    if (enabled && serverId) {
      connect();
    }

    return () => {
      disconnect();
    };
  }, [enabled, serverId, connect, disconnect]);

  return {
    isConnected,
    isConnecting,
    connectionError,
    entries,
    sendCommand,
    clearEntries,
    connect,
    disconnect,
  };
}
