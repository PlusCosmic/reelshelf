import type { ReactNode } from "react";
import { Link } from "@tanstack/react-router";
import { IconChevronLeft } from "@tabler/icons-react";
import { BrandLogo } from "@/components/Reelshelf/BrandLogo";
import { commonCopy } from "@/copy/common";
import { legalContactEmail, legalCopy } from "@/copy/legal";

export function LegalPage({
  title,
  updated,
  children,
}: {
  title: string;
  updated: string;
  children: ReactNode;
}) {
  return (
    <main className="rs-public-page rs-legal-page">
      <header className="rs-public-header">
        <Link to="/" className="rs-brand" aria-label={commonCopy.brand}>
          <BrandLogo />
        </Link>
      </header>

      <article className="rs-legal">
        <span className="rs-eyebrow">{legalCopy.lastUpdated(updated)}</span>
        <h1 className="rs-display rs-legal-title">{title}</h1>
        {children}
        <nav className="rs-legal-links" aria-label={commonCopy.legalNav}>
          <Link to="/terms">{commonCopy.termsLink}</Link>
          <Link to="/privacy">{commonCopy.privacyLink}</Link>
        </nav>
        <Link to="/" className="rs-signin-back">
          <IconChevronLeft size={14} aria-hidden="true" />
          {commonCopy.backToFrontPage}
        </Link>
      </article>
    </main>
  );
}

export function LegalContact() {
  return <a href={`mailto:${legalContactEmail}`}>{legalContactEmail}</a>;
}
