# Copy

Every word the site shows people lives here, so it can be read, reviewed and changed in one place
without hunting through components. The `reelshelf/no-jsx-copy` lint rule
(`frontend/eslint-rules/no-jsx-copy.js`) fails on words written straight into JSX anywhere outside
this folder: text, strings or templates in `{...}`, and strings given to attributes people read
(`aria-label`, `title`, `placeholder`, `alt`, `label`, `description`, ...). Tests are exempt.

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
