import { useState } from "react";
import type { CSSProperties } from "react";
import { IconBrandDiscord } from "@tabler/icons-react";
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

function loginWithDiscord() {
  const returnUrl = `${window.location.origin}/`;
  window.location.href = `/auth/discord/login?returnUrl=${encodeURIComponent(returnUrl)}`;
}

export function LandingPage() {
  const [hover, setHover] = useState(false);

  return (
    <div className="rs-landing">
      <div className="rs-landing-wash" />

      <header className="rs-landing-header">
        <div className="rs-brand" aria-label="Reelshelf">
          <BrandLogo />
        </div>
      </header>

      <main className="rs-landing-main">
        <div className="rs-landing-previews" aria-hidden="true">
          {previewClips.map((clip, index) => {
            return (
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
            );
          })}
        </div>

        <h1 className="rs-display rs-landing-title">
          Welcome <em>back</em>.
        </h1>
        <p className="rs-landing-copy">
          Your shelf is exactly where you left it.
        </p>

        <button
          className="rs-discord-login"
          type="button"
          onClick={loginWithDiscord}
          onMouseEnter={() => setHover(true)}
          onMouseLeave={() => setHover(false)}
          data-hover={hover ? "true" : "false"}
        >
          <IconBrandDiscord size={22} />
          Continue with Discord
        </button>
      </main>

      <footer className="rs-landing-footer">for friends, kept quietly</footer>
    </div>
  );
}
