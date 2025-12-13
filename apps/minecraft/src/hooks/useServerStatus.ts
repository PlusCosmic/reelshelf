import { useQuery } from '@tanstack/react-query';
import { getServerStatus, getOnlinePlayers } from '@repo/shared/services/minecraft';

/**
 * Fetches the current Minecraft server status
 * Polls every 30 seconds to keep status up-to-date
 * Uses 2 retries for resilience against temporary network issues
 */
export function useServerStatus() {
  return useQuery({
    queryKey: ['minecraft', 'status'],
    queryFn: getServerStatus,
    refetchInterval: 30000, // Poll every 30 seconds
    retry: 2,
  });
}

/**
 * Fetches the list of currently online players
 * Polls every 30 seconds to keep player list current
 * Uses 2 retries for resilience against temporary network issues
 */
export function useOnlinePlayers() {
  return useQuery({
    queryKey: ['minecraft', 'players'],
    queryFn: getOnlinePlayers,
    refetchInterval: 30000, // Poll every 30 seconds
    retry: 2,
  });
}
