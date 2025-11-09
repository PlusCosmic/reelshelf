
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
import { useCurrentUser, useLogout } from "../hooks/queries";
import tempLogo from "../assets/transparent plus.png";

function RootComponent() {
  const router = useRouterState();
  const isIndexRoute = router.location.pathname === '/';
  const { data: user, isLoading } = useCurrentUser();
  const logoutMutation = useLogout();

  return (
    <ModalsProvider>
      <AppShell
        withBorder={false}
        padding="md"
        header={{ height: 76 }}
        footer={{ height: 30 }}
        styles={{
          header: {
            background: 'linear-gradient(135deg, rgba(255, 255, 255, 0.05) 0%, rgba(255, 255, 255, 0.02) 100%)',
            backdropFilter: 'blur(10px)',
            borderBottom: '1px solid rgba(255, 255, 255, 0.1)',
          },
          footer: {
            background: 'linear-gradient(135deg, rgba(255, 255, 255, 0.05) 0%, rgba(255, 255, 255, 0.02) 100%)',
            backdropFilter: 'blur(10px)',
            borderTop: '1px solid rgba(255, 255, 255, 0.1)',
          }
        }}
      >
        <AppShell.Header>
          <Group m="md" gap={"sm"} justify={"space-between"}>
            <div>
              {!isIndexRoute && (
                <Link to={"/"} style={{ textDecoration: 'none', color: 'inherit' }}>
                  <Group gap={"xs"} style={{ transition: 'all 0.2s ease' }}>
                    <img
                      src={tempLogo}
                      width={40}
                      height={40}
                      referrerPolicy="no-referrer"
                      alt="Clips Logo"
                      style={{
                        filter: 'drop-shadow(0 2px 4px rgba(0, 0, 0, 0.2))',
                      }}
                    />
                    <Title>Clips</Title>
                  </Group>
                </Link>
              )}
            </div>
            <UserAvatar
              hideLogin={true}
              user={user}
              isLoading={isLoading}
              onLogout={() => logoutMutation.mutateAsync()}
            />
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