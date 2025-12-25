import { createFileRoute } from '@tanstack/react-router';
import { ConsoleTerminal } from '../../../components/console/ConsoleTerminal';

export const Route = createFileRoute('/servers/$serverId/console')({
  component: Console,
});

function Console() {
  return (
    <ConsoleTerminal />
  );
}
