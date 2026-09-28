import { createFileRoute } from "@tanstack/react-router";
import { CollectionsPage } from "@/components/Reelshelf/CollectionsPage";

export const Route = createFileRoute("/collections/")({
  component: CollectionsPage,
});
