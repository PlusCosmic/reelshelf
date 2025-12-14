import type { ReactNode } from 'react';
import { Link, useRouterState } from '@tanstack/react-router';
import { AppShell as MantineAppShell, Group, Title, NavLink, Stack } from '@mantine/core';
import { IconDashboard, IconTerminal, IconFolder } from '@tabler/icons-react';
import { UserAvatar } from '@repo/ui';
import { useCurrentUser, useLogout } from '../../hooks/queries';

interface AppShellProps {
  children: ReactNode;
}

export function AppShell({ children }: AppShellProps) {
  const router = useRouterState();
  const currentPath = router.location.pathname;
  const { data: user, isLoading } = useCurrentUser();
  const logoutMutation = useLogout();

  const navItems = [
    { path: '/', label: 'Dashboard', icon: IconDashboard },
    { path: '/console', label: 'Console', icon: IconTerminal },
    { path: '/files', label: 'Files', icon: IconFolder },
  ];

  return (
    <MantineAppShell
      withBorder={false}
      padding="xs"
      header={{ height: 76 }}
      navbar={{ width: 250, breakpoint: 'sm' }}
      styles={{
        header: {
          background: 'linear-gradient(135deg, rgba(255, 255, 255, 0.05) 0%, rgba(255, 255, 255, 0.02) 100%)',
          backdropFilter: 'blur(10px)',
          borderBottom: '1px solid rgba(255, 255, 255, 0.1)',
        },
        navbar: {
          background: 'linear-gradient(135deg, rgba(255, 255, 255, 0.05) 0%, rgba(255, 255, 255, 0.02) 100%)',
          backdropFilter: 'blur(10px)',
          borderRight: '1px solid rgba(255, 255, 255, 0.1)',
        },
      }}
    >
      <MantineAppShell.Header>
        <Group m="md" gap="sm" justify="space-between">
          <Group gap="xs">
            <Title size="h3">Minecraft Server</Title>
          </Group>
          <UserAvatar
            hideLogin={true}
            user={user}
            isLoading={isLoading}
            onLogout={() => logoutMutation.mutateAsync()}
          />
        </Group>
      </MantineAppShell.Header>

      <MantineAppShell.Navbar p="md">
        <Stack gap="xs">
          {navItems.map((item) => (
            <Link
              key={item.path}
              to={item.path}
              style={{ textDecoration: 'none' }}
            >
              <NavLink
                label={item.label}
                leftSection={<item.icon size={20} />}
                active={currentPath === item.path}
                styles={{
                  root: {
                    borderRadius: '8px',
                  },
                }}
              />
            </Link>
          ))}
        </Stack>
      </MantineAppShell.Navbar>

      <MantineAppShell.Main>{children}</MantineAppShell.Main>
    </MantineAppShell>
  );
}
