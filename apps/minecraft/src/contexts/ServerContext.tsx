import { createContext, useContext, useState, useEffect, type ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { getServers } from '@repo/shared/services/minecraft';
import type { MinecraftServer } from '@repo/nucleus-api-client';

interface ServerContextValue {
  /** The currently selected server ID */
  serverId: string | null;
  /** Set the current server */
  setServerId: (id: string) => void;
  /** List of available servers */
  servers: MinecraftServer[];
  /** Whether servers are loading */
  isLoading: boolean;
  /** Error loading servers */
  error: Error | null;
}

const ServerContext = createContext<ServerContextValue | null>(null);

interface ServerProviderProps {
  children: ReactNode;
}

/**
 * Provides server selection context to the minecraft app.
 * Fetches available servers and auto-selects the first one if none selected.
 */
export function ServerProvider({ children }: ServerProviderProps) {
  const [serverId, setServerId] = useState<string | null>(null);

  const { data: servers = [], isLoading, error } = useQuery({
    queryKey: ['minecraft', 'servers'],
    queryFn: getServers,
    staleTime: 60000, // 1 minute
  });

  // Auto-select first server when servers load
  useEffect(() => {
    if (!serverId && servers.length > 0 && servers[0].id) {
      setServerId(servers[0].id);
    }
  }, [serverId, servers]);

  return (
    <ServerContext.Provider
      value={{
        serverId,
        setServerId,
        servers,
        isLoading,
        error: error as Error | null,
      }}
    >
      {children}
    </ServerContext.Provider>
  );
}

/**
 * Hook to access the current server context.
 * Must be used within a ServerProvider.
 */
export function useServerContext(): ServerContextValue {
  const context = useContext(ServerContext);
  if (!context) {
    throw new Error('useServerContext must be used within a ServerProvider');
  }
  return context;
}

/**
 * Hook that returns the current server ID.
 * Throws if no server is selected (use in components that require a server).
 */
export function useServerId(): string {
  const { serverId } = useServerContext();
  if (!serverId) {
    throw new Error('No server selected');
  }
  return serverId;
}
