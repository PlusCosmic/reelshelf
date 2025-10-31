import { createRootRoute, Outlet } from '@tanstack/react-router'
import { TanStackRouterDevtoolsPanel } from '@tanstack/react-router-devtools'
import { TanStackDevtools } from '@tanstack/react-devtools'
import { ActionIcon, AppShell, Group } from '@mantine/core'
import { IconBrandGithub, IconBrandLinkedin } from '@tabler/icons-react'
import UserAvatar from '../components/UserAvatar'

export const Route = createRootRoute({
  component: () => (
    <AppShell
      withBorder={false}
      padding="md"
      header={{ height: 60 }}
      footer={{ height: 30 }}
    >
      <AppShell.Header>
        <Group m="xl">
          <UserAvatar />
        </Group>
      </AppShell.Header>
      <AppShell.Main>
        <Outlet />
      </AppShell.Main>
      <AppShell.Footer>
        <Group justify="center" align="center" h="100%">
          <ActionIcon
            component="a"
            href="https://github.com/PlusCosmic"
            target="_blank"
            rel="noopener noreferrer"
            variant="subtle"
            color="gray"
          >
            <IconBrandGithub size={20} />
          </ActionIcon>
          <ActionIcon
            component="a"
            href="https://www.linkedin.com/in/harry-lovesey-leach-445075195/"
            target="_blank"
            rel="noopener noreferrer"
            variant="subtle"
            color="gray"
          >
            <IconBrandLinkedin size={20} color="#0072b1" />
          </ActionIcon>
        </Group>
        <TanStackDevtools
          config={{
            position: 'bottom-right',
          }}
          plugins={[
            {
              name: 'Tanstack Router',
              render: <TanStackRouterDevtoolsPanel />,
            },
          ]}
        />
      </AppShell.Footer>
    </AppShell>
  ),
})
