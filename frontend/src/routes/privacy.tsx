import { createFileRoute } from "@tanstack/react-router";
import { LegalPage } from "@/components/Reelshelf/LegalPage";
import { PrivacyText, privacyCopy } from "@/copy/privacy";

export const Route = createFileRoute("/privacy")({
  component: PrivacyRoute,
});

function PrivacyRoute() {
  return (
    <LegalPage title={privacyCopy.title} updated={privacyCopy.updated}>
      <PrivacyText />
    </LegalPage>
  );
}
