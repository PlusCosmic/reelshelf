import { createFileRoute } from '@tanstack/react-router';
import { Container, Title, Text, Stack, SimpleGrid } from '@mantine/core';
import { ServerStatusCard } from '../components/dashboard/ServerStatusCard';
import { PlayerList } from '../components/dashboard/PlayerList';

export const Route = createFileRoute('/')({
  component: Dashboard,
});

function Dashboard() {
  return (
    <Container size="xl">
      <Stack gap="xl">
        <div>
          <Title order={1} mb="xs">Dashboard</Title>
          <Text c="dimmed">Monitor your Minecraft server status and players</Text>
        </div>

        <SimpleGrid cols={{ base: 1, md: 2 }} spacing="lg">
          <ServerStatusCard />
          <PlayerList />
        </SimpleGrid>
      </Stack>
    </Container>
  );
}
