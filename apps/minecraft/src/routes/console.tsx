import { createFileRoute } from '@tanstack/react-router';
import { Container, Title, Text, Stack } from '@mantine/core';
import { ConsoleTerminal } from '../components/console/ConsoleTerminal';

export const Route = createFileRoute('/console')({
  component: Console,
});

function Console() {
  return (
    <Container size="xl">
      <Stack gap="xl">
        <div>
          <Title order={1} mb="xs">Server Console</Title>
          <Text c="dimmed">Execute commands and view server output</Text>
        </div>

        <ConsoleTerminal />
      </Stack>
    </Container>
  );
}
