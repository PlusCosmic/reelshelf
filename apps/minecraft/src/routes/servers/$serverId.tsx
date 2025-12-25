import { createFileRoute, Outlet } from '@tanstack/react-router';

export const Route = createFileRoute('/servers/$serverId')({
  component: ServerLayout,
});

/**
 * Layout component for server-specific routes.
 * Wraps all /servers/:serverId/* routes and provides the serverId via route params.
 */
function ServerLayout() {
  return <Outlet />;
}
