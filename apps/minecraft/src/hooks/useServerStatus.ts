import { useQuery } from '@tanstack/react-query';
import { getServerStatus, getOnlinePlayers } from '@repo/shared/services/minecraft';

/**
 * Fetches the current Minecraft server status
 * Polls every 30 seconds to keep status up-to-date
 * Uses 2 retries for resilience against temporary network issues
 * @param serverId - The server ID to fetch status for
 */
export function useServerStatus(serverId: string | undefined) {
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
 * @param serverId - The server ID to fetch players for
 */
export function useOnlinePlayers(serverId: string | undefined) {
  return useQuery({
    queryKey: ['minecraft', 'players', serverId],
    queryFn: () => getOnlinePlayers(serverId!),
    refetchInterval: 30000, // Poll every 30 seconds
    retry: 2,
    enabled: !!serverId,
  });
}
