import { Navigate, createFileRoute, useNavigate } from "@tanstack/react-router";
import { DeviceApproval } from "@/components/Reelshelf/DeviceApproval";
import { useFeatures } from "@/hooks/featureFlags.queries";
import { isFeatureOn } from "@/shared/services/featureFlags";

type DeviceSearch = {
  /** The code the uploader shows, as it put it in the link it opened (KDRW-TPLM). */
  code?: string;
};

export const Route = createFileRoute("/device")({
  component: DeviceRoute,
  validateSearch: (search: Record<string, unknown>): DeviceSearch => ({
    code:
      typeof search.code === "string" && search.code.trim()
        ? search.code.trim()
        : undefined,
  }),
});

/** W1: approving a desktop uploader's sign-in. Signed-out visitors are sent through sign-in and back (`__root`). */
function DeviceRoute() {
  const { code } = Route.useSearch();
  const navigate = useNavigate();
  const features = useFeatures();

  // Behind the desktop-uploader flag; the API answers 404 without it too.
  if (features.isLoading) return null;
  if (!isFeatureOn(features.data, "desktop-uploader")) {
    return <Navigate to="/" replace />;
  }

  return (
    <DeviceApproval
      code={code}
      onCode={(typed) =>
        void navigate({ to: "/device", search: { code: typed } })
      }
    />
  );
}
