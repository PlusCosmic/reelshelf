import type { CSSProperties, ReactNode } from "react";
import { useState } from "react";
import { Link } from "@tanstack/react-router";
import {
  IconArrowRight,
  IconBooks,
  IconClock,
  IconDownload,
  IconFolderDown,
  IconLink,
  IconMoon,
  IconSun,
  IconUsers,
} from "@tabler/icons-react";
import { BrandLogo } from "@/components/Reelshelf/BrandLogo";
import { Bookcase } from "./Bookcase";
import type { GameShelfItem } from "./reelshelf-model";

const igdbCover = (id: string) =>
  `https://images.igdb.com/igdb/image/upload/t_cover_big/${id}.jpg`;

/** Example games for the shelf, bound in the cloth Reelshelf takes from each real cover. */
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
    id: "arc-raiders",
    tag: "clutch",
    duration: "0:38",
    x: -220,
    y: 20,
    rot: -6,
    width: 180,
  },
  {
    id: "marvel-rivals",
    tag: "fail",
    duration: "0:47",
    x: -60,
    y: 0,
    rot: 2,
    width: 200,
  },
  {
    id: "balatro",
    tag: "highlight",
    duration: "0:09",
    x: 80,
    y: 30,
    rot: -3,
    width: 170,
  },
  {
    id: "apex-legends",
    tag: "story",
    duration: "2:04",
    x: 220,
    y: 10,
    rot: 5,
    width: 180,
  },
];

const features: Array<{
  icon: ReactNode;
  title: string;
  copy: string;
}> = [
  {
    icon: <IconBooks size={20} aria-hidden="true" />,
    title: "Every game gets its own book",
    copy: "Your library is a shelf of games with real cover art, not a folder of filenames. Pick a game, filter by tag, or search, and see what's new since you last looked.",
  },
  {
    icon: <IconFolderDown size={20} aria-hidden="true" />,
    title: "Upload a whole night at once",
    copy: "Drag your recorder's folder onto the library and Reelshelf works out which game each clip is from. Review the queue, fix anything it got wrong, and let it run. Clips you've already uploaded are skipped.",
  },
  {
    icon: <IconClock size={20} aria-hidden="true" />,
    title: "Sessions sort themselves",
    copy: "Clips from the same game on the same night land in one collection without you lifting a finger. Rename it, tidy it, or leave it as the record of that evening.",
  },
  {
    icon: <IconUsers size={20} aria-hidden="true" />,
    title: "Collections you build with friends",
    copy: "Put the best moments in the order you want them watched, then invite the people who were there. They can watch everything in it and add their own clips alongside yours.",
  },
  {
    icon: <IconLink size={20} aria-hidden="true" />,
    title: "Share one clip with one link",
    copy: "Send a link and it plays. No account needed on their end, and the clip is only visible to people who have the link.",
  },
  {
    icon: <IconDownload size={20} aria-hidden="true" />,
    title: "Your clips stay yours",
    copy: "Download any clip back to your computer whenever you like. Reelshelf is somewhere to keep them, not somewhere they get stuck.",
  },
];

const steps = [
  {
    title: "Sign in",
    copy: "Use Discord or Twitch. There's no form to fill in and nothing to pay. Link the other one later and either login opens the same shelf.",
  },
  {
    title: "Drop your clips",
    copy: "Drag your recorder's folder onto the library. Review the queue, fix any game guesses, pick tags, and let it upload.",
  },
  {
    title: "Watch, tag, share",
    copy: "Sessions are already grouped. Build collections with friends, send one clip with one link, or download it back.",
  },
];

export function LandingPage({
  theme,
  onToggleTheme,
}: {
  theme?: "light" | "dark";
  onToggleTheme?: () => void;
}) {
  const nextTheme = theme === "dark" ? "light" : "dark";
  const [pulledOut, setPulledOut] = useState<string | null>("apex-legends");

  return (
    <div className="rs-landing">
      <header className="rs-landing-header">
        <div className="rs-brand" aria-label="Reelshelf">
          <BrandLogo />
        </div>
        <div className="rs-landing-header-actions">
          <Link to="/sign-in" className="rs-secondary rs-landing-header-signin">
            Sign in
          </Link>
          {onToggleTheme ? (
            <button
              className="rs-icon-button"
              type="button"
              onClick={onToggleTheme}
              aria-label={`Switch to ${nextTheme} mode`}
              title={`Switch to ${nextTheme} mode`}
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
                  <span className="rs-tag-badge">{clip.tag}</span>
                  <span className="rs-duration">{clip.duration}</span>
                </div>
              </div>
            ))}
          </div>

          <p className="rs-eyebrow rs-landing-eyebrow">
            A personal clip library
          </p>
          <h1 className="rs-display rs-landing-title">
            Every clip, <em>on one shelf</em>.
          </h1>
          <p className="rs-landing-copy">
            Reelshelf keeps your gameplay clips organised by game and by night,
            shares them one link at a time, and lets your friends help fill the
            shelf.
          </p>

          <Link to="/sign-in" className="rs-landing-cta-button">
            Get started
            <IconArrowRight size={18} aria-hidden="true" />
          </Link>
          <p className="rs-landing-fineprint">
            Free, with 25 GB of clip storage. Sign in with Discord or Twitch.
          </p>
        </section>

        <section
          className="rs-landing-shelf-demo"
          aria-label="Example shelf of games"
        >
          <div className="rs-landing-shelf-demo-head">
            <span className="rs-eyebrow">Your archive</span>
            <span className="rs-landing-shelf-demo-stat">
              415 clips across 6 games. Pull one out.
            </span>
          </div>
          <Bookcase
            shelf={shelfBooks}
            selectedId={pulledOut}
            onSelect={(game) => setPulledOut(game.id)}
            onPutBack={() => setPulledOut(null)}
          />
        </section>

        <section className="rs-landing-section" id="features">
          <div className="rs-landing-section-head">
            <span className="rs-eyebrow">What's on the shelf</span>
            <h2 className="rs-display rs-landing-h2">
              Built for the clips you <em>actually</em> go back to.
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
            <span className="rs-eyebrow">How it works</span>
            <h2 className="rs-display rs-landing-h2">
              Three steps, <em>no setup</em>.
            </h2>
          </div>
          <ol className="rs-landing-steps">
            {steps.map((step, index) => (
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
            Your shelf is <em>waiting</em>.
          </h2>
          <p className="rs-landing-copy">
            Sign in, drop a folder, and the first session sorts itself.
          </p>
          <Link to="/sign-in" className="rs-landing-cta-button">
            Get started
            <IconArrowRight size={18} aria-hidden="true" />
          </Link>
        </section>
      </main>

      <footer className="rs-landing-footer">
        for the clips worth keeping
        <nav className="rs-landing-footer-links" aria-label="Legal">
          <Link to="/terms">Terms</Link>
          <Link to="/privacy">Privacy</Link>
        </nav>
      </footer>
    </div>
  );
}
