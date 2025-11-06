
import {
  Link,
  Outlet,
  createRootRoute,
  useRouterState,
} from "@tanstack/react-router";
import { AppShell, Group, Title } from '@mantine/core'
import { Footer, UserAvatar } from "@repo/ui";
import { Notifications } from "@mantine/notifications";
import { ModalsProvider } from '@mantine/modals';
import tempLogo from "../assets/transparent plus.png";

function RootComponent() {
  const router = useRouterState();
  const isIndexRoute = router.location.pathname === '/';

  return (
    <ModalsProvider>
      <AppShell
        withBorder={false}
        padding="md"
        header={{ height: 60 }}
        footer={{ height: 30 }}
      >
        <AppShell.Header>
          <Group m="md" gap={"sm"} justify={"space-between"}>
            <div>
              {!isIndexRoute && (
                <Link to={"/"} style={{ textDecoration: 'none', color: 'inherit' }}>
                  <Group gap={"xs"}>
                    <img
                      src={tempLogo}
                      width={40}
                      height={40}
                      referrerPolicy="no-referrer"
                      alt=""
                    />
                    <Title>Clips</Title>
                  </Group>
                </Link>
              )}
            </div>
            <UserAvatar hideLogin={true} />
          </Group>
        </AppShell.Header>
        <AppShell.Main>
          <Outlet />
        </AppShell.Main>
        <AppShell.Footer>
          <Footer/>
          <Notifications/>
        </AppShell.Footer>
      </AppShell>
    </ModalsProvider>
  );
}

export const Route = createRootRoute({
  component: RootComponent,
})