import { createFileRoute } from "@tanstack/react-router";
import { BulkUploadQueue } from "@/components/Upload/BulkUploadQueue";

export const Route = createFileRoute("/upload")({
  component: UploadRoute,
});

function UploadRoute() {
  return <BulkUploadQueue />;
}
