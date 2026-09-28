import { createFileRoute, redirect } from "@tanstack/react-router";

// Collections lived at /playlists before; old links still land in the right place.
export const Route = createFileRoute("/playlists/$playlistId")({
  beforeLoad: ({ params }) => {
    throw redirect({
      to: "/collections/$playlistId",
      params: { playlistId: params.playlistId },
      replace: true,
    });
  },
});
