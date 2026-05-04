import type { ReactNode } from 'react';
import { Link, useRouterState } from '@tanstack/react-router';
import {
  AppShell as MantineAppShell,
  Group,
  Title,
  Text,
  NavLink,
  Stack,
  Box,
  ThemeIcon,
  Badge,
  Tooltip,
  ActionIcon,
  Burger,
} from '@mantine/core';
import { useDisclosure, useMediaQuery } from '@mantine/hooks';
import {
  IconDashboard,
  IconTerminal2,
  IconFolderCode,
  IconServer2,
  IconActivity,
  IconBrandGithub,
  IconBrandLinkedin,
  IconList,
  IconCloudUpload,
  IconSettings,
} from '@tabler/icons-react';
import { UserAvatar } from '@repo/ui';
import { useCurrentUser, useLogout } from '../../hooks/queries';
import { useServerStatus } from '../../hooks/useServerStatus';
import { useServerContext } from '../../contexts/ServerContext';

interface AppShellProps {
  children: ReactNode;
}

export function AppShell({ children }: AppShellProps) {
  const [navbarOpened, { toggle: toggleNavbar, close: closeNavbar }] = useDisclosure();
  const isMobile = useMediaQuery('(max-width: 48em)');

  const router = useRouterState();
  const currentPath = router.location.pathname;
  const { data: user, isLoading } = useCurrentUser();
  const logoutMutation = useLogout();

  // Try to get serverId from route params (if we're on a server-specific route)
  const params = router.matches[router.matches.length - 1]?.params as { serverId?: string } | undefined;
  const serverId = params?.serverId;

  const { data: serverStatus } = useServerStatus(serverId);
  const { servers } = useServerContext();

  // Find current server name
  const currentServer = servers.find(s => s.id === serverId);

  // Navigation items change based on whether we're on a server route
  const navItems = serverId ? [
    { path: `/servers/${serverId}`, label: 'Dashboard', icon: IconDashboard, description: 'Server overview' },
    { path: `/servers/${serverId}/console`, label: 'Console', icon: IconTerminal2, description: 'Terminal access' },
    { path: `/servers/${serverId}/files`, label: 'Files', icon: IconFolderCode, description: 'File manager' },
    { path: `/servers/${serverId}/backups`, label: 'Backups', icon: IconCloudUpload, description: 'Backup management' },
    { path: `/servers/${serverId}/settings`, label: 'Settings', icon: IconSettings, description: 'Server configuration' },
  ] : [
    { path: '/servers', label: 'Servers', icon: IconList, description: 'Select server' },
  ];

  return (
    <MantineAppShell
      withBorder={false}
      padding="md"
      header={{ height: 70 }}
      navbar={{ width: 260, breakpoint: 'sm', collapsed: { mobile: !navbarOpened } }}
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
        navbar: {
          background: 'linear-gradient(180deg, rgba(10, 10, 20, 0.95) 0%, rgba(15, 15, 30, 0.9) 100%)',
          backdropFilter: 'blur(20px)',
          borderRight: '1px solid rgba(0, 212, 255, 0.1)',
        },
        footer: {
          background: 'linear-gradient(90deg, rgba(0, 212, 255, 0.02) 0%, rgba(168, 85, 247, 0.03) 50%, rgba(0, 212, 255, 0.02) 100%)',
          backdropFilter: 'blur(10px)',
          borderTop: '1px solid rgba(0, 212, 255, 0.1)',
        },
      }}
    >
      <MantineAppShell.Header>
        <Group h="100%" px={{ base: 'sm', sm: 'lg' }} justify="space-between">
          {/* Left side - Burger (mobile) + Logo and Title */}
          <Group gap={isMobile ? 'xs' : 'md'}>
            <Burger
              opened={navbarOpened}
              onClick={toggleNavbar}
              hiddenFrom="sm"
              size="sm"
              color="#00d4ff"
            />
            <Box
              visibleFrom="xs"
              style={{
                position: 'relative',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
              }}
            >
              <ThemeIcon
                size={isMobile ? 36 : 44}
                radius="md"
                variant="gradient"
                gradient={{ from: 'cyberBlue', to: 'cyberPurple', deg: 135 }}
                style={{
                  boxShadow: '0 0 20px rgba(0, 212, 255, 0.4)',
                }}
              >
                <IconServer2 size={isMobile ? 20 : 26} stroke={1.5} />
              </ThemeIcon>
            </Box>
            <Stack gap={0}>
              <Title
                order={4}
                size={isMobile ? 'sm' : 'h4'}
                style={{
                  background: 'linear-gradient(90deg, #00d4ff 0%, #a855f7 100%)',
                  WebkitBackgroundClip: 'text',
                  WebkitTextFillColor: 'transparent',
                  fontWeight: 700,
                }}
              >
                {currentServer?.name || 'MINECRAFT PANEL'}
              </Title>
              <Group gap="xs" visibleFrom="sm">
                {serverId && (
                  <>
                    <Box
                      style={{
                        width: 6,
                        height: 6,
                        borderRadius: '50%',
                        backgroundColor: serverStatus?.isOnline ? '#00ff88' : '#ff4444',
                        boxShadow: serverStatus?.isOnline
                          ? '0 0 8px rgba(0, 255, 136, 0.8)'
                          : '0 0 8px rgba(255, 68, 68, 0.8)',
                      }}
                    />
                    <Text size="xs" c="dimmed" fw={500}>
                      {serverStatus?.isOnline ? 'Online' : 'Offline'}
                    </Text>
                  </>
                )}
                {!serverId && (
                  <Text size="xs" c="dimmed" fw={500}>
                    Select a server to manage
                  </Text>
                )}
              </Group>
            </Stack>
          </Group>

          {/* Right side - Status and User */}
          <Group gap={isMobile ? 'sm' : 'lg'}>
            <Box visibleFrom="sm">
              <Tooltip label="Server activity">
                <Badge
                  variant="dot"
                  color={serverStatus?.isOnline ? 'green' : 'red'}
                  size="lg"
                  style={{
                    cursor: 'default',
                    background: 'rgba(0, 212, 255, 0.1)',
                    border: '1px solid rgba(0, 212, 255, 0.2)',
                  }}
                >
                  <Group gap={6}>
                    <IconActivity size={14} />
                    <Text size="xs" fw={600}>
                      {serverStatus?.onlinePlayers ?? 0} / {serverStatus?.maxPlayers ?? 0}
                    </Text>
                  </Group>
                </Badge>
              </Tooltip>
            </Box>
            <UserAvatar
              hideLogin={true}
              user={user}
              isLoading={isLoading}
              onLogout={() => logoutMutation.mutateAsync()}
            />
          </Group>
        </Group>
      </MantineAppShell.Header>

      <MantineAppShell.Navbar p="md">
        <Stack gap="xs" mt="xs">
          {/* Back to Servers link when on a server-specific route */}
          {serverId && (
            <Link to="/servers" style={{ textDecoration: 'none' }} onClick={closeNavbar}>
              <NavLink
                label={
                  <Text size="sm" fw={500}>
                    All Servers
                  </Text>
                }
                description={
                  <Text size="xs" c="dimmed">
                    Back to server list
                  </Text>
                }
                leftSection={
                  <ThemeIcon
                    size="lg"
                    radius="md"
                    variant="light"
                    color="cyberBlue"
                  >
                    <IconList size={18} />
                  </ThemeIcon>
                }
                variant="subtle"
                styles={{
                  root: {
                    borderRadius: 10,
                    padding: '12px',
                    marginBottom: 8,
                  },
                }}
              />
            </Link>
          )}

          {/* Navigation Label */}
          <Text
            size="xs"
            fw={600}
            c="dimmed"
            tt="uppercase"
            pl="sm"
            mb="xs"
            style={{ letterSpacing: '0.1em' }}
          >
            {serverId ? 'Server Management' : 'Navigation'}
          </Text>

          {/* Nav Items */}
          {navItems.map((item) => {
            const isActive = currentPath === item.path;
            return (
              <Link
                key={item.path}
                to={item.path}
                style={{ textDecoration: 'none' }}
                onClick={closeNavbar}
              >
                <NavLink
                  label={
                    <Text size="sm" fw={isActive ? 600 : 500}>
                      {item.label}
                    </Text>
                  }
                  description={
                    <Text size="xs" c="dimmed">
                      {item.description}
                    </Text>
                  }
                  leftSection={
                    <ThemeIcon
                      size="lg"
                      radius="md"
                      variant={isActive ? 'gradient' : 'light'}
                      gradient={isActive ? { from: 'cyberBlue', to: 'cyberPurple', deg: 135 } : undefined}
                      color={isActive ? undefined : 'cyberBlue'}
                      style={
                        isActive
                          ? { boxShadow: '0 0 15px rgba(0, 212, 255, 0.4)' }
                          : {}
                      }
                    >
                      <item.icon
                        size={18}
                        style={
                          isActive
                            ? { filter: 'drop-shadow(0 0 4px rgba(255, 255, 255, 0.5))' }
                            : {}
                        }
                      />
                    </ThemeIcon>
                  }
                  active={isActive}
                  variant={isActive ? 'filled' : 'subtle'}
                  styles={{
                    root: {
                      borderRadius: 10,
                      padding: '12px',
                      background: isActive
                        ? 'linear-gradient(90deg, rgba(0, 212, 255, 0.15) 0%, rgba(168, 85, 247, 0.1) 100%)'
                        : 'transparent',
                      borderLeft: isActive
                        ? '3px solid #00d4ff'
                        : '3px solid transparent',
                      boxShadow: isActive
                        ? 'inset 0 0 30px rgba(0, 212, 255, 0.05)'
                        : 'none',
                      transition: 'all 0.2s ease',
                      '&:hover': {
                        background: isActive
                          ? undefined
                          : 'rgba(0, 212, 255, 0.05)',
                      },
                    },
                    label: {
                      color: isActive ? '#00d4ff' : 'inherit',
                    },
                  }}
                />
              </Link>
            );
          })}
        </Stack>

        {/* Bottom Section - Server Info (only show on server routes) */}
        {serverId && serverStatus && (
          <Box mt="auto" pt="xl">
            <Box
              p="md"
              style={{
                background: 'linear-gradient(135deg, rgba(0, 212, 255, 0.05) 0%, rgba(168, 85, 247, 0.08) 100%)',
                borderRadius: 12,
                border: '1px solid rgba(0, 212, 255, 0.1)',
              }}
            >
              <Stack gap="xs">
                <Group justify="space-between">
                  <Text size="xs" c="dimmed" tt="uppercase" fw={600}>
                    Version
                  </Text>
                  <Badge
                    size="sm"
                    variant="light"
                    color="cyberPurple"
                  >
                    {serverStatus.version || 'Unknown'}
                  </Badge>
                </Group>
                {serverStatus.motd && (
                  <Text
                    size="xs"
                    c="dimmed"
                    lineClamp={2}
                    style={{ fontStyle: 'italic' }}
                  >
                    "{serverStatus.motd}"
                  </Text>
                )}
              </Stack>
            </Box>
          </Box>
        )}
      </MantineAppShell.Navbar>

      <MantineAppShell.Main>{children}</MantineAppShell.Main>

      <MantineAppShell.Footer>
        <Group h="100%" px={{ base: 'sm', sm: 'lg' }} justify="space-between">
          <Text size="xs" c="dimmed">
            <span style={{ color: '#00d4ff' }}>Game Server Panel</span>{' '}
            <Box component="span" visibleFrom="xs">by PlusCosmic</Box>
          </Text>
          <Group gap={isMobile ? 'xs' : 'md'}>
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
            <Box visibleFrom="sm">
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
            </Box>
          </Group>
        </Group>
      </MantineAppShell.Footer>
    </MantineAppShell>
  );
}
