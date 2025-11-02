import { createFileRoute } from "@tanstack/react-router";
import { ApexClip } from "../../components/Apex/ApexClip.tsx";

export const Route = createFileRoute("/apex-legends/$clipId")({
  component: RouteComponent,
});

function RouteComponent() {
  const { clipId } = Route.useParams();
  return <ApexClip clipId={clipId} />;
}
