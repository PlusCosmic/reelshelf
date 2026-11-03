import { createFileRoute } from "@tanstack/react-router";
import { BinPage } from "@/components/Reelshelf/BinPage";

export const Route = createFileRoute("/bin")({
  component: BinPage,
});
