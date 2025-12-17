import { SimpleGrid, Box, Text, Group, ThemeIcon, Stack, RingProgress } from '@mantine/core';
import {
  IconUsers,
  IconCpu,
  IconDatabase,
  IconClock,
} from '@tabler/icons-react';
import { useServerStatus } from '../../hooks/useServerStatus';

interface MetricCardProps {
  icon: React.ReactNode;
  label: string;
  value: string | number;
  subValue?: string;
  color: string;
  glowColor: string;
  progress?: number;
}

function MetricCard({ icon, label, value, subValue, color, glowColor, progress }: MetricCardProps) {
  return (
    <Box
      p="lg"
      style={{
        background: 'linear-gradient(135deg, rgba(15, 15, 25, 0.9) 0%, rgba(20, 20, 35, 0.8) 100%)',
        borderRadius: 14,
        border: `1px solid ${glowColor}33`,
        position: 'relative',
        overflow: 'hidden',
        transition: 'all 0.3s ease',
      }}
      className="cyber-card"
    >
      {/* Top accent line */}
      <Box
        style={{
          position: 'absolute',
          top: 0,
          left: 0,
          right: 0,
          height: 2,
          background: `linear-gradient(90deg, ${color} 0%, ${glowColor} 100%)`,
          opacity: 0.8,
        }}
      />

      <Group justify="space-between" align="flex-start">
        <Stack gap="xs">
          <Text size="xs" c="dimmed" tt="uppercase" fw={600} style={{ letterSpacing: '0.05em' }}>
            {label}
          </Text>
          <Group gap="xs" align="baseline">
            <Text
              size="xl"
              fw={700}
              style={{
                color,
                textShadow: `0 0 20px ${glowColor}80`,
              }}
            >
              {value}
            </Text>
            {subValue && (
              <Text size="sm" c="dimmed">
                {subValue}
              </Text>
            )}
          </Group>
        </Stack>

        {progress !== undefined ? (
          <RingProgress
            size={50}
            thickness={4}
            roundCaps
            sections={[{ value: progress, color }]}
            rootColor="rgba(255,255,255,0.1)"
          />
        ) : (
          <ThemeIcon
            size={44}
            radius="md"
            variant="light"
            style={{
              background: `${glowColor}15`,
              border: `1px solid ${glowColor}30`,
              color,
            }}
          >
            {icon}
          </ThemeIcon>
        )}
      </Group>
    </Box>
  );
}

export function ServerMetrics() {
  const { data: status, isLoading } = useServerStatus();

  // Calculate player percentage
  const playerPercentage = status?.maxPlayers
    ? Math.round((status.onlinePlayers / status.maxPlayers) * 100)
    : 0;

  // Mock data for demo - in production these would come from your API
  const cpuUsage = 45;
  const memoryUsage = 62;
  const uptime = '4d 12h';

  if (isLoading) {
    return (
      <SimpleGrid cols={{ base: 2, md: 4 }} spacing="md">
        {[...Array(4)].map((_, i) => (
          <Box
            key={i}
            h={100}
            style={{
              background: 'rgba(15, 15, 25, 0.8)',
              borderRadius: 14,
              border: '1px solid rgba(0, 212, 255, 0.1)',
            }}
            className="cyber-loading"
          />
        ))}
      </SimpleGrid>
    );
  }

  return (
    <SimpleGrid cols={{ base: 2, md: 4 }} spacing="md">
      <MetricCard
        icon={<IconUsers size={22} />}
        label="Players Online"
        value={status?.onlinePlayers ?? 0}
        subValue={`/ ${status?.maxPlayers ?? 0}`}
        color="#00d4ff"
        glowColor="#00d4ff"
        progress={playerPercentage}
      />
      <MetricCard
        icon={<IconCpu size={22} />}
        label="CPU Usage"
        value={`${cpuUsage}%`}
        color="#a855f7"
        glowColor="#a855f7"
        progress={cpuUsage}
      />
      <MetricCard
        icon={<IconDatabase size={22} />}
        label="Memory"
        value={`${memoryUsage}%`}
        color="#ec4899"
        glowColor="#ec4899"
        progress={memoryUsage}
      />
      <MetricCard
        icon={<IconClock size={22} />}
        label="Uptime"
        value={uptime}
        color="#00ff88"
        glowColor="#00ff88"
      />
    </SimpleGrid>
  );
}
