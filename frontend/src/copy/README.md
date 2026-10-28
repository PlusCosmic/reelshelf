# Copy

Every word the site shows people lives here, so it can be read, reviewed and changed in one place
without hunting through components. The `reelshelf/no-jsx-copy` lint rule
(`frontend/eslint-rules/no-jsx-copy.js`) fails on words written straight into JSX anywhere outside
this folder: text, strings or templates in `{...}`, and strings given to attributes people read
(`aria-label`, `title`, `placeholder`, `alt`, `label`, `description`, ...). Tests are exempt.

## Voice

Reelshelf is the friend who keeps the good tapes: they were there that night, they kept the footage,
and they can find it again when you want it. They play games, so they know what a clutch or a squad
night is, but they don't need to shout about it. Every other clip app sells the moment of capture
and the race to post it; Reelshelf is about keeping clips, and it talks like it.

1. **Quiet confidence over hype.** No "ultimate", "epic", "insane" or "magically", and no
   exclamation marks. Describe the thing well and trust it to be good.
2. **Plain words, said once.** Short, active sentences. Say what happened and what to do next.
   Don't pass on an engineer's words (ETag, protocol, modal, IGDB, encode) to someone who didn't ask.
3. **The shelf is the metaphor; use it lightly.** Keep to the words below and don't pile them up:
   "Add a game", not "Add a new book to your bookshelf category".
4. **Warm when it matters, straight when it counts.** Empty states, a finished upload and the
   greeting can have a little character. Errors, billing, deletion and moderation are calm and
   exact, and never joke.

### Words we use

| Word        | Means                                                                                       |
| ----------- | ------------------------------------------------------------------------------------------- |
| library     | Everything someone keeps on Reelshelf. Not "archive".                                       |
| shelf       | The bookcase of games at the top of the library, and the place clips land ("on the shelf"). |
| book        | One game's clips on the shelf, or a category someone named themselves ("Snowboarding").     |
| clip        | One video. Not "video" unless we mean the file itself.                                      |
| session     | The clips from one game on one night, grouped for you.                                      |
| collection  | Clips someone put together, in their order, perhaps with friends.                           |
| share link  | The link that lets anyone who has it watch one clip.                                        |
| plan        | Free, Plus, Pro or Unlimited; how many clips a library keeps and how long each can be.      |
| sign in/out | The verbs (never "log in" or "log out"); "sign-in" is the noun, as in "linked sign-ins".    |

### House style

- **British English**: organised, colour, uncategorised, theatre.
- **Contractions** wherever they read naturally: "couldn't", "you'll", "isn't".
- **Errors say what didn't happen, then what to do**: "Your library didn't load. Try again." Not
  "The archive could not be loaded." Say "nothing changed" or "nothing was charged" when that is
  true, because it's the first thing people want to know.
- **Sentence case** for headings, buttons and labels. Buttons are verbs: "Add clips", "Share",
  "Make collection".
- **We and you**: "we" is Reelshelf, "you" is the person reading. Clips are "your clips".
- **Punctuation**: straight apostrophes and quotes in source (`'` and `"`), so the copy stays easy
  to search and to type; `·` separates facts on one line; `…` ends anything still happening
  ("Uploading…") and a button that opens a further step ("Delete account…"); no exclamation marks.
- **Numbers** go through `formatCount` or `toLocaleString`; spell out "one" only in a sentence that
  reads better for it ("One new clip is waiting").

## Layout

One module per area of the site, named after it, exporting one object named `<area>Copy`:

| Module                                                                | Covers                                                                               |
| --------------------------------------------------------------------- | ------------------------------------------------------------------------------------ |
| `common.ts`, `plural.ts`                                              | Words used everywhere: brand, Loading…, Cancel, Close, the theme switch, clip counts |
| `planLimits.ts`                                                       | The plan limits copy quotes (mirrors `TierLimits`; not copy, but read by it)         |
| `landing.tsx`, `signIn.tsx`                                           | The signed-out front page and sign-in                                                |
| `legal.tsx`, `terms.tsx`, `privacy.tsx`                               | The legal pages' frame and their full text                                           |
| `shell.ts`, `ui.ts`                                                   | The signed-in app's chrome (nav, profile menu) and the generic UI components         |
| `library.tsx`, `clip.ts`, `video.ts`                                  | The shelf and library, the clip page, and the player's states                        |
| `collections.ts`, `share.tsx`, `reportReasons.ts`                     | Collections, the share page and its report form                                      |
| `upload.ts`, `twitchImport.tsx`, `addGame.ts`                         | Uploading, Twitch import and adding a game                                           |
| `plan.tsx`, `settings.tsx`                                            | Settings, the plan and billing, `/plan`                                              |
| `activity.ts`                                                         | The Discord Activity watch room                                                      |
| `admin.tsx`, `legendDetection.tsx`, `clipTranscription.ts`, `runs.ts` | The admin pages and internal review tools (`runs.ts` holds words both tools share)   |

Import a module directly (`import { libraryCopy } from "@/copy/library"`). There is no index file,
so each chunk only pulls in the copy of the components it uses.

## Writing entries

- **Plain text** is a string: `title: "Upload a whole night at once"`.
- **Text with values** is a function taking them: `clipsAcross: (clips: number, games: number) => ...`.
  Counts go through `plural` or `commonCopy.clips`; numbers people read go through `formatCount`.
- **Emphasis** (`<em>`, `<strong>`, `<code>`) is a JSX fragment in a `.tsx` module:
  `title: <>Every clip, <em>on one shelf</em>.</>`.
- **Links or components inside a sentence** are passed in by the component, so the copy keeps the
  wording and the component keeps the routing, with the link text as its own entry:
  `agreeToTerms: (terms: ReactNode, privacy: ReactNode) => <>By continuing you agree to the {terms} and {privacy}.</>`.
- **Long documents** (the terms and privacy policy) are kept whole, as components in their own
  module, because splitting prose into keys makes it harder to review, not easier.
- Keep entries in the order they appear on the page, and group a page's parts in nested objects.
- Names, data and identifiers aren't copy: game names, provider names from the API, CSS classes,
  query keys and log messages stay where they are.

When changing what the privacy policy or terms promise, update their "last updated" date too.
