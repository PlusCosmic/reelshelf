import { createFileRoute } from "@tanstack/react-router";
import { LegalPage } from "@/components/Reelshelf/LegalPage";
import { TermsText, termsCopy } from "@/copy/terms";

export const Route = createFileRoute("/terms")({
  component: TermsRoute,
});

function TermsRoute() {
  return (
    <LegalPage title={termsCopy.title} updated={termsCopy.updated}>
      <TermsText />
    </LegalPage>
  );
}
