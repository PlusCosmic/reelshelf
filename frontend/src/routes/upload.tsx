import { createFileRoute } from "@tanstack/react-router";
import { useState } from "react";
import { BulkUploadQueue } from "@/components/Upload/BulkUploadQueue";
import { consumePendingBulkUploadEntry } from "@/utils/bulkUploadEntry";

export const Route = createFileRoute("/upload")({
  component: UploadRoute,
});

function UploadRoute() {
  const [entry] = useState(() => consumePendingBulkUploadEntry());

  return (
    <BulkUploadQueue
      fallbackCategoryId={entry?.fallbackCategoryId}
      initialFiles={entry?.files}
    />
  );
}
