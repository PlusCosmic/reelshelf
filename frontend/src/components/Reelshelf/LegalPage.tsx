import type { ReactNode } from "react";
import { Link } from "@tanstack/react-router";
import { IconChevronLeft } from "@tabler/icons-react";
import { BrandLogo } from "@/components/Reelshelf/BrandLogo";

// TODO: set the contact address before these pages go live.
export const legalOperator = "PlusCosmic";
export const legalContactEmail = "[contact email]";
export const legalGoverningLaw = "England and Wales";

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
        <Link to="/" className="rs-brand" aria-label="Reelshelf">
          <BrandLogo />
        </Link>
      </header>

      <article className="rs-legal">
        <span className="rs-eyebrow">Last updated {updated}</span>
        <h1 className="rs-display rs-legal-title">{title}</h1>
        {children}
        <nav className="rs-legal-links" aria-label="Legal">
          <Link to="/terms">Terms of service</Link>
          <Link to="/privacy">Privacy policy</Link>
        </nav>
        <Link to="/" className="rs-signin-back">
          <IconChevronLeft size={14} aria-hidden="true" />
          Back to the front page
        </Link>
      </article>
    </main>
  );
}

export function LegalContact() {
  return <a href={`mailto:${legalContactEmail}`}>{legalContactEmail}</a>;
}
