import { Outlet, createRootRoute } from '@tanstack/react-router'
import { TanStackRouterDevtoolsPanel } from '@tanstack/react-router-devtools'
import { TanStackDevtools } from '@tanstack/react-devtools'
import { AppShell, Group } from '@mantine/core'
import { Footer, UserAvatar } from "@repo/ui";
import { Notifications } from "@mantine/notifications";
import { ModalsProvider } from '@mantine/modals';

export const Route = createRootRoute({
  component: () => (
    <ModalsProvider>
      <AppShell
        withBorder={false}
        padding="md"
        header={{ height: 60 }}
        footer={{ height: 30 }}
      >
        <AppShell.Header>
          <Group m="xl">
            <UserAvatar hideLogin={true} />
          </Group>
        </AppShell.Header>
        <AppShell.Main>
          <Outlet />
        </AppShell.Main>
        <AppShell.Footer>
          <Footer/>
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
          <Notifications/>
        </AppShell.Footer>
      </AppShell>
    </ModalsProvider>
  ),
})
