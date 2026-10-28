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
    copy: "Reelshelf keeps your gameplay clips organised by game and by night, shares them one link at a time, and lets your friends help fill the shelf.",
    cta: "Get started",
    fineprint: `Free for up to ${formatCount(planLimits.free.clips)} clips. Sign in with Discord or Twitch.`,
  },
  /** Tags on the example clips floating over the hero, by the example game's slug. */
  previewTags: {
    "arc-raiders": "clutch",
    "marvel-rivals": "fail",
    balatro: "highlight",
    "apex-legends": "story",
  },
  shelfDemo: {
    label: "Example shelf of games",
    eyebrow: "Your archive",
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
        title: "Every game gets its own book",
        copy: "Your library is a shelf of games with real cover art, not a folder of filenames. Pick a game, filter by tag, or search, and see what's new since you last looked.",
      },
      bulkUpload: {
        title: "Upload a whole night at once",
        copy: "Drag your recorder's folder onto the library and Reelshelf works out which game each clip is from. Review the queue, fix anything it got wrong, and let it run. Clips you've already uploaded are skipped.",
      },
      sessions: {
        title: "Sessions sort themselves",
        copy: "Clips from the same game on the same night land in one collection without you lifting a finger. Rename it, tidy it, or leave it as the record of that evening.",
      },
      collections: {
        title: "Collections you build with friends",
        copy: "Put the best moments in the order you want them watched, then invite the people who were there. They can watch everything in it and add their own clips alongside yours.",
      },
      sharing: {
        title: "Share one clip with one link",
        copy: "Send a link and it plays. No account needed on their end, and the clip is only visible to people who have the link.",
      },
      downloads: {
        title: "Your clips stay yours",
        copy: "Download any clip back to your computer whenever you like. Reelshelf is somewhere to keep them, not somewhere they get stuck.",
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
    ],
  },
  closing: {
    title: (
      <>
        Your shelf is <em>waiting</em>.
      </>
    ),
    copy: "Sign in, drop a folder, and the first session sorts itself.",
  },
  footer: {
    tagline: "for the clips worth keeping",
    terms: "Terms",
    privacy: "Privacy",
  },
} as const;
