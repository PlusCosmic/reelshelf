import { createFileRoute } from '@tanstack/react-router';
import { ConsoleTerminal } from '../components/console/ConsoleTerminal';

export const Route = createFileRoute('/console')({
  component: Console,
});

function Console() {
  return (
    <ConsoleTerminal />
  );
}
