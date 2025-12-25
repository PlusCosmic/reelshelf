import { createFileRoute, Navigate } from '@tanstack/react-router';

export const Route = createFileRoute('/')({
  component: Index,
});

/**
 * Root index route - redirects to server selection page
 */
function Index() {
  return <Navigate to="/servers" />;
}
