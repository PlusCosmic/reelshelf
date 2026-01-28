import { createFileRoute } from '@tanstack/react-router';
import { FileBrowser } from '../../../components/files/FileBrowser';

export const Route = createFileRoute('/servers/$serverId/files')({
  component: Files,
});

function Files() {
  return (
    <FileBrowser />
  );
}
