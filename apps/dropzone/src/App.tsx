import { AppShell, Container, Title, Text, Stack } from '@mantine/core'
import { Footer } from '@repo/ui'

export default function App() {
  return (
    <AppShell
      header={{ height: 60 }}
      footer={{ height: 60 }}
      padding="md"
    >
      <AppShell.Header>
        <Container h="100%" display="flex" style={{ alignItems: 'center' }}>
          <Title order={2}>PlusCosmic Dropzone</Title>
        </Container>
      </AppShell.Header>

      <AppShell.Main>
        <Container size="lg">
          <Stack gap="xl" py="xl">
            <div>
              <Title order={1}>Welcome to Dropzone</Title>
              <Text size="lg" c="dimmed" mt="sm">
                Your new React application is ready to go!
              </Text>
            </div>

            <Text>
              This app is configured with:
            </Text>
            <ul>
              <li>React 19</li>
              <li>Vite for fast development</li>
              <li>Mantine UI components</li>
              <li>React Query for data fetching</li>
              <li>React Router DOM for routing</li>
              <li>TypeScript</li>
            </ul>
          </Stack>
        </Container>
      </AppShell.Main>

      <AppShell.Footer>
        <Footer />
      </AppShell.Footer>
    </AppShell>
  )
}
