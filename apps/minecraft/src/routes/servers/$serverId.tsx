import { createFileRoute, Outlet, useParams } from '@tanstack/react-router';

export const Route = createFileRoute('/servers/$serverId')({
  component: ServerLayout,
});

/**
 * Layout component for server-specific routes.
 * Wraps all /servers/:serverId/* routes and provides the serverId via route params.
 */
function ServerLayout() {
  const { serverId } = useParams({ from: '/servers/$serverId' });

  // The serverId is now available to all child routes via useParams
  return <Outlet />;
}
