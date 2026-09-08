import type { CSSProperties, ReactNode } from "react";
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

const previewClips = [
  {
    id: "valorant",
    tag: "clutch",
    duration: "0:38",
    colorA: "oklch(0.56 0.16 24)",
    colorB: "oklch(0.31 0.08 350)",
    x: -220,
    y: 20,
    rot: -6,
    width: 180,
  },
  {
    id: "helldivers",
    tag: "fail",
    duration: "0:47",
    colorA: "oklch(0.58 0.13 82)",
    colorB: "oklch(0.34 0.07 52)",
    x: -60,
    y: 0,
    rot: 2,
    width: 200,
  },
  {
    id: "rocket",
    tag: "highlight",
    duration: "0:09",
    colorA: "oklch(0.54 0.14 288)",
    colorB: "oklch(0.32 0.09 312)",
    x: 80,
    y: 30,
    rot: -3,
    width: 170,
  },
  {
    id: "bg3",
    tag: "story",
    duration: "2:04",
    colorA: "oklch(0.59 0.12 116)",
    colorB: "oklch(0.32 0.07 92)",
    x: 220,
    y: 10,
    rot: 5,
    width: 180,
  },
];

const shelfBooks = [
  {
    name: "Apex Legends",
    count: 142,
    a: "oklch(0.56 0.16 24)",
    b: "oklch(0.31 0.08 350)",
  },
  {
    name: "Helldivers 2",
    count: 58,
    a: "oklch(0.58 0.13 82)",
    b: "oklch(0.34 0.07 52)",
  },
  {
    name: "Rocket League",
    count: 91,
    a: "oklch(0.54 0.14 288)",
    b: "oklch(0.32 0.09 312)",
  },
  {
    name: "Baldur's Gate 3",
    count: 23,
    a: "oklch(0.59 0.12 116)",
    b: "oklch(0.32 0.07 92)",
  },
  {
    name: "Deep Rock Galactic",
    count: 37,
    a: "oklch(0.6 0.13 55)",
    b: "oklch(0.33 0.07 35)",
  },
  {
    name: "Valorant",
    count: 64,
    a: "oklch(0.52 0.12 210)",
    b: "oklch(0.3 0.06 230)",
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

  return (
    <div className="rs-landing">
      <div className="rs-landing-wash" />

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
                    "--game-a": clip.colorA,
                    "--game-b": clip.colorB,
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
              415 clips across 6 games
            </span>
          </div>
          <div className="rs-shelf">
            <div className="rs-shelf-track">
              {shelfBooks.map((book) => (
                <div
                  key={book.name}
                  className="rs-spine rs-landing-spine"
                  style={
                    { "--game-a": book.a, "--game-b": book.b } as CSSProperties
                  }
                  title={book.name}
                >
                  <span>{book.name}</span>
                  <span>{book.count}</span>
                </div>
              ))}
            </div>
          </div>
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

      <footer className="rs-landing-footer">for the clips worth keeping</footer>
    </div>
  );
}
