import { createFileRoute, useNavigate } from "@tanstack/react-router";
import { IconBrandTwitch, IconUpload } from "@tabler/icons-react";
import { useEffect, useState } from "react";
import { describeLinkResult } from "@/components/Reelshelf/LinkedAccounts";
import { BulkUploadQueue } from "@/components/Upload/BulkUploadQueue";
import { TwitchClipsPanel } from "@/components/Upload/TwitchClipsPanel";
import { consumePendingBulkUploadEntry } from "@/utils/bulkUploadEntry";

type ClipSource = "files" | "twitch";

type UploadSearch = {
  source?: ClipSource;
  linked?: string;
  link_error?: string;
};

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
  const [entry] = useState(() => consumePendingBulkUploadEntry());
  const search = Route.useSearch();
  const navigate = useNavigate();
  const source: ClipSource = search.source ?? "files";
  // Linking Twitch from this page comes back here with the result in the query string; keep it in state
  // and clean the URL so a refresh doesn't repeat the message.
  const [notice] = useState(() => describeLinkResult(search));

  useEffect(() => {
    if (search.linked || search.link_error) {
      void navigate({
        to: "/upload",
        search: { source: search.source },
        replace: true,
      });
    }
  }, [navigate, search.link_error, search.linked, search.source]);

  function selectSource(next: ClipSource) {
    void navigate({
      to: "/upload",
      search: next === "twitch" ? { source: "twitch" } : {},
      replace: true,
    });
  }

  return (
    <div className="rs-add-clips">
      <nav className="rs-add-clips-tabs" aria-label="Clip source">
        <button
          className={`rs-chip${source === "files" ? " active" : ""}`}
          type="button"
          aria-pressed={source === "files"}
          onClick={() => selectSource("files")}
        >
          <IconUpload size={14} /> Upload files
        </button>
        <button
          className={`rs-chip${source === "twitch" ? " active" : ""}`}
          type="button"
          aria-pressed={source === "twitch"}
          onClick={() => selectSource("twitch")}
        >
          <IconBrandTwitch size={14} /> Twitch clips
        </button>
      </nav>

      {source === "twitch" ? (
        <TwitchClipsPanel notice={notice} />
      ) : (
        <BulkUploadQueue
          fallbackCategoryId={entry?.fallbackCategoryId}
          initialFiles={entry?.files}
        />
      )}
    </div>
  );
}
