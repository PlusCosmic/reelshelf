import { commonCopy } from "./common";
import { formatCount, planLimits } from "./planLimits";

/** The signed-out front page, in the order it reads. */
export const landingCopy = {
  signIn: "Sign in",
  hero: {
    eyebrow: "A personal clip library",
    title: (
      <>
        Every clip, <em>on one shelf</em>.
      </>
    ),
    copy: "Your gameplay clips, sorted by game and by night. Share one with a link, or watch them back with friends on Discord.",
    cta: "Start your shelf",
    fineprint: `Free for up to ${formatCount(planLimits.free.clips)} clips. Sign in with Discord, Twitch or Google.`,
  },
  /** Tags on the example clips floating over the hero, by the example game's slug. */
  previewTags: {
    "arc-raiders": "clutch",
    "marvel-rivals": "fail",
    balatro: "highlight",
    "apex-legends": "story",
  },
  shelfDemo: {
    label: "An example shelf",
    eyebrow: "Your library",
    stat: (clips: number, games: number) =>
      `${commonCopy.clips(clips)} across ${games} games. Pull one out.`,
  },
  features: {
    eyebrow: "What's on the shelf",
    title: (
      <>
        Built for the clips you <em>actually</em> go back to.
      </>
    ),
    items: {
      books: {
        title: "A book for every game",
        copy: "Each with its real cover on the spine.",
      },
      bulkUpload: {
        title: "Drop in a whole night",
        copy: "Drag a folder onto a book. Twitch clips copy straight across.",
      },
      sessions: {
        title: "Sessions by night",
        copy: "One game, one evening, kept together.",
      },
      collections: {
        title: "Collections with friends",
        copy: "Set the order, then let your friends add theirs.",
      },
      watchRoom: {
        title: "Watch together on Discord",
        copy: "Queue clips from your shelves and watch them in the voice channel.",
      },
      sharing: {
        title: "One clip, one link",
        copy: "It plays for anyone you send it to. No account needed.",
      },
    },
  },
  steps: {
    eyebrow: "How it works",
    title: (
      <>
        Three steps, <em>no setup</em>.
      </>
    ),
    items: [
      {
        title: "Sign in",
        copy: "With Discord, Twitch or Google. No card needed.",
      },
      {
        title: "Drop your clips",
        copy: "Drag a folder onto a game's book, or bring them over from Twitch.",
      },
      {
        title: "Watch them back",
        copy: "Each night is already grouped. Press play.",
      },
    ],
  },
  closing: {
    title: (
      <>
        Your shelf is <em>waiting</em>.
      </>
    ),
    copy: "Sign in and drop in your first night.",
  },
  footer: {
    tagline: "for the clips worth keeping",
    copyright: (year: number) => `© ${year} Reelshelf. All rights reserved.`,
    terms: "Terms",
    privacy: "Privacy",
  },
} as const;
