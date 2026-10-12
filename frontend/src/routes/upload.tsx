import { createFileRoute, useNavigate } from "@tanstack/react-router";
import { useEffect, useState } from "react";
import { describeLinkResult } from "@/components/Reelshelf/LinkedAccounts";
import { TwitchClipsPanel } from "@/components/Upload/TwitchClipsPanel";

type UploadSearch = {
  source?: "twitch";
  linked?: string;
  link_error?: string;
};

// Clips from your computer are dropped straight onto a book on the shelf; this page is what's left
// of the old upload page: picking through your Twitch clips one by one.
export const Route = createFileRoute("/upload")({
  component: UploadRoute,
  validateSearch: (search: Record<string, unknown>): UploadSearch => ({
    source: search.source === "twitch" ? "twitch" : undefined,
    linked: typeof search.linked === "string" ? search.linked : undefined,
    link_error:
      typeof search.link_error === "string" ? search.link_error : undefined,
  }),
});

function UploadRoute() {
  const search = Route.useSearch();
  const navigate = useNavigate();
  // Linking Twitch from this page comes back here with the result in the query string; keep it in state
  // and clean the URL so a refresh doesn't repeat the message.
  const [notice] = useState(() => describeLinkResult(search));

  useEffect(() => {
    if (search.linked || search.link_error) {
      void navigate({
        to: "/upload",
        search: { source: "twitch" },
        replace: true,
      });
    }
  }, [navigate, search.link_error, search.linked]);

  return (
    <div className="rs-add-clips">
      <TwitchClipsPanel notice={notice} />
    </div>
  );
}
