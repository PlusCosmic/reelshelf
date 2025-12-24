import { createFileRoute } from '@tanstack/react-router';
import { Container, Title, Text, Stack, SimpleGrid, Box, Group } from '@mantine/core';
import { ServerStatusCard } from '../../../components/dashboard/ServerStatusCard';
import { PlayerList } from '../../../components/dashboard/PlayerList';
import { QuickActions } from '../../../components/dashboard/QuickActions';
import { ServerMetrics } from '../../../components/dashboard/ServerMetrics';

export const Route = createFileRoute('/servers/$serverId/')({
  component: Dashboard,
});

function Dashboard() {
  return (
    <Container size="xl" py="md">
      <Stack gap="xl">
        {/* Header */}
        <Box>
          <Group gap="md" mb="xs">
            <Box
              style={{
                width: 4,
                height: 32,
                borderRadius: 2,
                background: 'linear-gradient(180deg, #00d4ff 0%, #a855f7 100%)',
                boxShadow: '0 0 10px rgba(0, 212, 255, 0.5)',
              }}
            />
            <Box>
              <Title
                order={2}
                style={{
                  background: 'linear-gradient(90deg, #ffffff 0%, #a0a0a0 100%)',
                  WebkitBackgroundClip: 'text',
                  WebkitTextFillColor: 'transparent',
                }}
              >
                Dashboard
              </Title>
              <Text size="sm" c="dimmed">
                Monitor and control your Minecraft server
              </Text>
            </Box>
          </Group>
        </Box>

        {/* Metrics Row */}
        <ServerMetrics />

        {/* Main Content Grid */}
        <SimpleGrid cols={{ base: 1, md: 2, lg: 3 }} spacing="lg">
          {/* Server Status - Takes up one column */}
          <ServerStatusCard />

          {/* Player List - Takes up one column */}
          <PlayerList />

          {/* Quick Actions - Takes up one column */}
          <QuickActions />
        </SimpleGrid>
      </Stack>
    </Container>
  );
}
