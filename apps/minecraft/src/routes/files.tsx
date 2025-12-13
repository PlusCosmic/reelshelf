import { createFileRoute } from '@tanstack/react-router';
import { Container, Title, Text, Stack } from '@mantine/core';
import { FileBrowser } from '../components/files/FileBrowser';

export const Route = createFileRoute('/files')({
  component: Files,
});

function Files() {
  return (
    <Container size="xl">
      <Stack gap="xl">
        <div>
          <Title order={1} mb="xs">File Manager</Title>
          <Text c="dimmed">Browse and manage server files</Text>
        </div>

        <FileBrowser />
      </Stack>
    </Container>
  );
}
