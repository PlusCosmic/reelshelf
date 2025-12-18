import { useQuery } from '@tanstack/react-query';
import { getServerStatus, getOnlinePlayers } from '@repo/shared/services/minecraft';
import { useServerContext } from '../contexts/ServerContext';

/**
 * Fetches the current Minecraft server status
 * Polls every 30 seconds to keep status up-to-date
 * Uses 2 retries for resilience against temporary network issues
 */
export function useServerStatus() {
  const { serverId } = useServerContext();

  return useQuery({
    queryKey: ['minecraft', 'status', serverId],
    queryFn: () => getServerStatus(serverId!),
    refetchInterval: 30000, // Poll every 30 seconds
    retry: 2,
    enabled: !!serverId,
  });
}

/**
 * Fetches the list of currently online players
 * Polls every 30 seconds to keep player list current
 * Uses 2 retries for resilience against temporary network issues
 */
export function useOnlinePlayers() {
  const { serverId } = useServerContext();

  return useQuery({
    queryKey: ['minecraft', 'players', serverId],
    queryFn: () => getOnlinePlayers(serverId!),
    refetchInterval: 30000, // Poll every 30 seconds
    retry: 2,
    enabled: !!serverId,
  });
}
