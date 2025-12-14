import { Outlet, createRootRoute } from "@tanstack/react-router";
import { Notifications } from "@mantine/notifications";
import { ModalsProvider } from '@mantine/modals';
import { Center, Loader } from '@mantine/core';
import { AppShell } from "../components/layout/AppShell";
import { LoginScreen } from "../components/layout/LoginScreen";
import { useCurrentUser } from "../hooks/queries";

function RootComponent() {
  const { data: user, isLoading } = useCurrentUser();

  // Show loading state while checking auth
  if (isLoading) {
    return (
      <Center h="100vh" style={{ background: 'var(--mantine-color-dark-8)' }}>
        <Loader size="lg" />
      </Center>
    );
  }

  // Show login screen if not authenticated
  if (!user) {
    return <LoginScreen />;
  }

  return (
    <ModalsProvider>
      <AppShell>
        <Outlet />
      </AppShell>
      <Notifications />
    </ModalsProvider>
  );
}

export const Route = createRootRoute({
  component: RootComponent,
})
