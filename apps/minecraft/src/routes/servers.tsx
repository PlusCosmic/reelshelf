import { createFileRoute, Outlet, useMatches } from '@tanstack/react-router';
import { Container, Title, Text, Stack, SimpleGrid, Box, Group, Button, Loader, Center } from '@mantine/core';
import { IconPlus, IconServer2 } from '@tabler/icons-react';
import { useServerContext } from '../contexts/ServerContext';
import { ServerCard } from '../components/servers/ServerCard';
import { useState } from 'react';
import { CreateServerModal } from '../components/servers/CreateServerModal';

export const Route = createFileRoute('/servers')({
  component: ServersLayout,
});

/**
 * Layout component for /servers routes.
 * Shows server selection when on /servers, or renders child routes via Outlet.
 */
function ServersLayout() {
  const matches = useMatches();
  // Check if we have a child route (more than just /servers)
  const hasChildRoute = matches.some(match => match.routeId.startsWith('/servers/$'));

  if (hasChildRoute) {
    // Render child route content
    return <Outlet />;
  }

  // Show server selection when on /servers directly
  return <ServerSelection />;
}

function ServerSelection() {
  const { servers, isLoading, error } = useServerContext();
  const [createModalOpen, setCreateModalOpen] = useState(false);

  if (isLoading) {
    return (
      <Center h="calc(100vh - 200px)">
        <Stack align="center" gap="md">
          <Loader size="xl" />
          <Text c="dimmed">Loading servers...</Text>
        </Stack>
      </Center>
    );
  }

  if (error) {
    return (
      <Center h="calc(100vh - 200px)">
        <Stack align="center" gap="md">
          <Text c="red" size="lg">Failed to load servers</Text>
          <Text c="dimmed" size="sm">{error.message}</Text>
        </Stack>
      </Center>
    );
  }

  return (
    <Container size="xl" py="md">
      <Stack gap="xl">
        {/* Header */}
        <Box>
          <Group justify="space-between" mb="xs">
            <Group gap="md">
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
                  Server Selection
                </Title>
                <Text size="sm" c="dimmed">
                  Choose a server to manage or create a new one
                </Text>
              </Box>
            </Group>

            <Button
              leftSection={<IconPlus size={18} />}
              variant="gradient"
              gradient={{ from: 'cyberBlue', to: 'cyberPurple', deg: 135 }}
              onClick={() => setCreateModalOpen(true)}
              style={{
                boxShadow: '0 0 20px rgba(0, 212, 255, 0.3)',
              }}
            >
              Create Server
            </Button>
          </Group>
        </Box>

        {/* Server Grid */}
        {servers.length === 0 ? (
          <Center h="400px">
            <Stack align="center" gap="md">
              <IconServer2 size={64} stroke={1} style={{ opacity: 0.3 }} />
              <Text c="dimmed" size="lg">No servers found</Text>
              <Text c="dimmed" size="sm">Create your first Minecraft server to get started</Text>
              <Button
                leftSection={<IconPlus size={18} />}
                variant="light"
                color="cyberBlue"
                onClick={() => setCreateModalOpen(true)}
                mt="md"
              >
                Create Your First Server
              </Button>
            </Stack>
          </Center>
        ) : (
          <SimpleGrid cols={{ base: 1, sm: 2, lg: 3 }} spacing="lg">
            {servers.map((server) => (
              <ServerCard key={server.id} server={server} />
            ))}
          </SimpleGrid>
        )}
      </Stack>

      <CreateServerModal
        opened={createModalOpen}
        onClose={() => setCreateModalOpen(false)}
      />
    </Container>
  );
}
