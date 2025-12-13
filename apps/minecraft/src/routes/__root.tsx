import { Outlet, createRootRoute } from "@tanstack/react-router";
import { Notifications } from "@mantine/notifications";
import { ModalsProvider } from '@mantine/modals';
import { AppShell } from "../components/layout/AppShell";

function RootComponent() {
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
