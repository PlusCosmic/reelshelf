import { createFileRoute, redirect } from "@tanstack/react-router";

// Collections lived at /playlists before; old links still land in the right place.
export const Route = createFileRoute("/playlists/")({
  beforeLoad: () => {
    throw redirect({ to: "/collections", replace: true });
  },
});
