import {
  Link,
  Outlet,
  createRootRoute,
  useRouterState,
} from "@tanstack/react-router";
import { ActionIcon, AppShell, Box, Group, Text, Title, Tooltip } from '@mantine/core'
import { IconBrandGithub, IconBrandLinkedin } from '@tabler/icons-react'
import { UserAvatar } from "@repo/ui";
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
        header={{ height: 70 }}
        footer={{ height: 36 }}
        className="cyber-background"
        styles={{
          main: {
            background: 'transparent',
            minHeight: '100vh',
          },
          header: {
            background: 'linear-gradient(90deg, rgba(0, 212, 255, 0.03) 0%, rgba(168, 85, 247, 0.05) 50%, rgba(0, 212, 255, 0.03) 100%)',
            backdropFilter: 'blur(20px)',
            borderBottom: '1px solid rgba(0, 212, 255, 0.15)',
            boxShadow: '0 4px 30px rgba(0, 0, 0, 0.3)',
          },
          footer: {
            background: 'linear-gradient(90deg, rgba(0, 212, 255, 0.02) 0%, rgba(168, 85, 247, 0.03) 50%, rgba(0, 212, 255, 0.02) 100%)',
            backdropFilter: 'blur(10px)',
            borderTop: '1px solid rgba(0, 212, 255, 0.1)',
          }
        }}
      >
        <AppShell.Header>
          <Group h="100%" px="lg" justify="space-between">
            <div>
              {!isIndexRoute && (
                <Link to={"/"} style={{ textDecoration: 'none', color: 'inherit' }}>
                  <Group gap="sm" style={{ transition: 'all 0.2s ease' }}>
                    <Box
                      style={{
                        position: 'relative',
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'center',
                      }}
                    >
                      <img
                        src={tempLogo}
                        width={40}
                        height={40}
                        referrerPolicy="no-referrer"
                        alt="Clips Logo"
                        style={{
                          filter: 'drop-shadow(0 0 8px rgba(0, 212, 255, 0.5))',
                        }}
                      />
                    </Box>
                    <Title
                      order={3}
                      style={{
                        background: 'linear-gradient(90deg, #00d4ff 0%, #a855f7 100%)',
                        WebkitBackgroundClip: 'text',
                        WebkitTextFillColor: 'transparent',
                        fontWeight: 700,
                        letterSpacing: '-0.5px',
                      }}
                    >
                      Clips
                    </Title>
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
          <Group h="100%" px="lg" justify="space-between">
            <Text size="xs" c="dimmed">
              <span style={{ color: '#00d4ff' }}>Clips</span> by PlusCosmic
            </Text>
            <Group gap="md">
              <Tooltip label="GitHub" position="top">
                <ActionIcon
                  component="a"
                  href="https://github.com/PlusCosmic"
                  target="_blank"
                  rel="noopener noreferrer"
                  variant="subtle"
                  size="sm"
                  style={{
                    color: '#00d4ff',
                    transition: 'all 0.2s ease',
                  }}
                >
                  <IconBrandGithub size={18} />
                </ActionIcon>
              </Tooltip>
              <Tooltip label="LinkedIn" position="top">
                <ActionIcon
                  component="a"
                  href="https://www.linkedin.com/in/harry-lovesey-leach-445075195/"
                  target="_blank"
                  rel="noopener noreferrer"
                  variant="subtle"
                  size="sm"
                  style={{
                    color: '#a855f7',
                    transition: 'all 0.2s ease',
                  }}
                >
                  <IconBrandLinkedin size={18} />
                </ActionIcon>
              </Tooltip>
              <Text size="xs" c="dimmed">
                Powered by{' '}
                <span
                  style={{
                    background: 'linear-gradient(90deg, #00d4ff 0%, #a855f7 100%)',
                    WebkitBackgroundClip: 'text',
                    WebkitTextFillColor: 'transparent',
                  }}
                >
                  Nucleus
                </span>
              </Text>
            </Group>
          </Group>
          <Notifications />
        </AppShell.Footer>
      </AppShell>
    </ModalsProvider>
  );
}

export const Route = createRootRoute({
  component: RootComponent,
})
