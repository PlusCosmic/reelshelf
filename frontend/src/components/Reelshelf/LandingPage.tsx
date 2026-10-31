import type { CSSProperties, ReactNode } from "react";
import { useMemo } from "react";
import { Link } from "@tanstack/react-router";
import {
  IconArrowRight,
  IconBooks,
  IconBrandDiscord,
  IconClock,
  IconFolderDown,
  IconLink,
  IconMoon,
  IconPlayerPause,
  IconPlayerPlay,
  IconSun,
  IconUsers,
} from "@tabler/icons-react";
import { BrandLogo } from "@/components/Reelshelf/BrandLogo";
import { commonCopy } from "@/copy/common";
import { landingCopy } from "@/copy/landing";
import { useLandingShelf } from "@/hooks/landingShelf.queries";
import { Bookcase } from "./Bookcase";
import { LandingSideShelves } from "./LandingSideShelves";
import { shelfCycleMs, shelfFromPopularGames } from "./landingShelf";
import type { GameShelfItem } from "./reelshelf-model";
import { useShelfCycle } from "./useShelfCycle";

const igdbCover = (id: string) =>
  `https://images.igdb.com/igdb/image/upload/t_cover_big/${id}.jpg`;

/**
 * Example games, bound in the cloth Reelshelf takes from each real cover. The polaroids above the shelf use their
 * colours, and the shelf falls back to them when today's popular games can't be had.
 */
const shelfBooks: GameShelfItem[] = (
  [
    ["Apex Legends", 142, 31, "#b06c51", "coa93z"],
    ["Marvel Rivals", 91, 4, "#928111", "cobxmn"],
    ["Balatro", 23, 0, "#326295", "co9f4g"],
    ["Slay the Spire II", 37, 2, "#2f2e57", "co82c5"],
    ["Minecraft", 58, 0, "#5a9148", "co8fu7"],
    ["ARC Raiders", 64, 0, "#382541", "co9rk1"],
  ] as const
).map(([name, clipCount, unviewedCount, clothColor, cover]) => {
  const slug = name.toLowerCase().replace(/[^a-z0-9]+/g, "-");
  return {
    id: slug,
    name,
    slug,
    coverUrl: igdbCover(cover),
    keyArtUrl: null,
    gameLogoUrl: null,
    isCustom: false,
    clothColor,
    clipCount,
    unviewedCount,
    durationSeconds: 0,
    sizeBytes: 0,
  };
});

const clothOf = (slug: string) =>
  shelfBooks.find((book) => book.slug === slug)?.clothColor ?? "#1f6b45";

const previewClips = [
  {
    id: "arc-raiders" as const,
    duration: "0:38",
    x: -220,
    y: 20,
    rot: -6,
    width: 180,
  },
  {
    id: "marvel-rivals" as const,
    duration: "0:47",
    x: -60,
    y: 0,
    rot: 2,
    width: 200,
  },
  {
    id: "balatro" as const,
    duration: "0:09",
    x: 80,
    y: 30,
    rot: -3,
    width: 170,
  },
  {
    id: "apex-legends" as const,
    duration: "2:04",
    x: 220,
    y: 10,
    rot: 5,
    width: 180,
  },
];

const featureIcons: Record<keyof typeof landingCopy.features.items, ReactNode> =
  {
    books: <IconBooks size={20} aria-hidden="true" />,
    bulkUpload: <IconFolderDown size={20} aria-hidden="true" />,
    sessions: <IconClock size={20} aria-hidden="true" />,
    collections: <IconUsers size={20} aria-hidden="true" />,
    watchRoom: <IconBrandDiscord size={20} aria-hidden="true" />,
    sharing: <IconLink size={20} aria-hidden="true" />,
  };

const features = (
  Object.keys(featureIcons) as Array<keyof typeof featureIcons>
).map((key) => ({
  icon: featureIcons[key],
  ...landingCopy.features.items[key],
}));

/**
 * The example shelf: a random dozen of today's popular games from IGDB, a different dozen on each visit, pulling
 * out one book after another. Until they load it stands empty (it is below the fold), and if they can't be had it
 * shows the built-in books instead.
 */
function ShelfDemo() {
  const popular = useLandingShelf();
  const popularGames = popular.data;
  const fromPopular = (popularGames?.length ?? 0) > 0;
  const shelf = useMemo(
    () =>
      popularGames && popularGames.length > 0
        ? shelfFromPopularGames(popularGames)
        : null,
    [popularGames],
  );
  const books = shelf ?? (popular.isPending ? [] : shelfBooks);
  const ids = useMemo(() => books.map((book) => book.id), [books]);
  const cycle = useShelfCycle(ids, shelfCycleMs);
  const selected = books.find((book) => book.id === cycle.selectedId);
  const clipCount = books.reduce((total, book) => total + book.clipCount, 0);

  return (
    <section
      className="rs-landing-shelf-demo"
      aria-label={landingCopy.shelfDemo.label}
    >
      <div className="rs-landing-shelf-demo-head">
        <span className="rs-eyebrow">{landingCopy.shelfDemo.eyebrow}</span>
        {books.length > 0 ? (
          <span className="rs-landing-shelf-demo-stat">
            {landingCopy.shelfDemo.stat(clipCount, books.length)}
          </span>
        ) : null}
      </div>
      <div {...cycle.holdProps}>
        <Bookcase
          shelf={books}
          selectedId={cycle.selectedId}
          onSelect={(game) => cycle.select(game.id)}
          onPutBack={cycle.putBack}
          footer={
            <>
              {cycle.canMove ? (
                <button
                  type="button"
                  className="rs-icon-button rs-landing-shelf-toggle"
                  onClick={cycle.toggleStopped}
                  aria-label={
                    cycle.stopped
                      ? landingCopy.shelfDemo.start
                      : landingCopy.shelfDemo.stop
                  }
                  title={
                    cycle.stopped
                      ? landingCopy.shelfDemo.start
                      : landingCopy.shelfDemo.stop
                  }
                >
                  {cycle.stopped ? (
                    <IconPlayerPlay size={14} aria-hidden="true" />
                  ) : (
                    <IconPlayerPause size={14} aria-hidden="true" />
                  )}
                </button>
              ) : null}
              {selected ? (
                <span className="rs-landing-shelf-caption" key={selected.id}>
                  <span className="rs-display rs-landing-shelf-caption-name">
                    {selected.name}
                  </span>
                  {fromPopular ? (
                    <span className="rs-landing-shelf-caption-meta">
                      {landingCopy.shelfDemo.popularToday}
                    </span>
                  ) : null}
                </span>
              ) : null}
            </>
          }
        />
      </div>
    </section>
  );
}

export function LandingPage({
  theme,
  onToggleTheme,
}: {
  theme?: "light" | "dark";
  onToggleTheme?: () => void;
}) {
  const nextTheme = theme === "dark" ? "light" : "dark";

  return (
    <div className="rs-landing">
      <LandingSideShelves />
      <header className="rs-landing-header">
        <div className="rs-brand" aria-label={commonCopy.brand}>
          <BrandLogo />
        </div>
        <div className="rs-landing-header-actions">
          <Link to="/sign-in" className="rs-secondary rs-landing-header-signin">
            {landingCopy.signIn}
          </Link>
          {onToggleTheme ? (
            <button
              className="rs-icon-button"
              type="button"
              onClick={onToggleTheme}
              aria-label={commonCopy.switchTheme(nextTheme)}
              title={commonCopy.switchTheme(nextTheme)}
            >
              {theme === "dark" ? (
                <IconSun size={16} />
              ) : (
                <IconMoon size={16} />
              )}
            </button>
          ) : null}
        </div>
      </header>

      <main className="rs-landing-main">
        <section className="rs-landing-hero">
          <div className="rs-landing-previews" aria-hidden="true">
            {previewClips.map((clip, index) => (
              <div
                className="rs-landing-polaroid"
                key={clip.id}
                style={
                  {
                    "--game-a": clothOf(clip.id),
                    "--game-b": `color-mix(in oklab, ${clothOf(clip.id)} 45%, black)`,
                    "--preview-width": `${clip.width}px`,
                    "--preview-x": `${clip.x}px`,
                    "--preview-y": `${clip.y}px`,
                    "--preview-rot": `${clip.rot}deg`,
                    "--preview-drift-delay": `${index * -1.7}s`,
                  } as CSSProperties
                }
              >
                <div className="rs-landing-thumb">
                  <span className="rs-tag-badge">
                    {landingCopy.previewTags[clip.id]}
                  </span>
                  <span className="rs-duration">{clip.duration}</span>
                </div>
              </div>
            ))}
          </div>

          <p className="rs-eyebrow rs-landing-eyebrow">
            {landingCopy.hero.eyebrow}
          </p>
          <h1 className="rs-display rs-landing-title">
            {landingCopy.hero.title}
          </h1>
          <p className="rs-landing-copy">{landingCopy.hero.copy}</p>

          <Link to="/sign-in" className="rs-landing-cta-button">
            {landingCopy.hero.cta}
            <IconArrowRight size={18} aria-hidden="true" />
          </Link>
        </section>

        <ShelfDemo />

        <section className="rs-landing-section" id="features">
          <div className="rs-landing-section-head">
            <h2 className="rs-display rs-landing-h2">
              {landingCopy.features.title}
            </h2>
          </div>
          <div className="rs-landing-features">
            {features.map((feature) => (
              <article className="rs-landing-feature" key={feature.title}>
                <span className="rs-landing-feature-icon">{feature.icon}</span>
                <h3>{feature.title}</h3>
                <p>{feature.copy}</p>
              </article>
            ))}
          </div>
        </section>

        <section className="rs-landing-section" id="how-it-works">
          <div className="rs-landing-section-head">
            <h2 className="rs-display rs-landing-h2">
              {landingCopy.steps.title}
            </h2>
          </div>
          <ol className="rs-landing-steps">
            {landingCopy.steps.items.map((step, index) => (
              <li className="rs-landing-step" key={step.title}>
                <span className="rs-landing-step-number">{index + 1}</span>
                <h3>{step.title}</h3>
                <p>{step.copy}</p>
              </li>
            ))}
          </ol>
        </section>

        <section className="rs-landing-section rs-landing-cta">
          <h2 className="rs-display rs-landing-h2">
            {landingCopy.closing.title}
          </h2>
          <Link to="/sign-in" className="rs-landing-cta-button">
            {landingCopy.hero.cta}
            <IconArrowRight size={18} aria-hidden="true" />
          </Link>
        </section>
      </main>

      <footer className="rs-landing-footer">
        {landingCopy.footer.tagline}
        <div className="rs-landing-footer-row">
          <span className="rs-landing-footer-copyright">
            <BrandLogo wordmark={false} />
            {landingCopy.footer.copyright(new Date().getFullYear())}
          </span>
          <nav
            className="rs-landing-footer-links"
            aria-label={commonCopy.legalNav}
          >
            <Link to="/terms">{landingCopy.footer.terms}</Link>
            <Link to="/privacy">{landingCopy.footer.privacy}</Link>
          </nav>
        </div>
      </footer>
    </div>
  );
}
